import sys
import tempfile
import threading
import time
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PLUGINS = ROOT.parent
sys.path.insert(0, str(ROOT))
sys.path.insert(0, str(PLUGINS / "heaven-task-queue"))

from heaven_cluster import ClusterScheduler, QueueClusterDispatcher
from heaven_task_queue import TaskQueue


class CountingQueue(TaskQueue):
    def __init__(self, database):
        super().__init__(database)
        self.renew_count = 0
        self._renew_lock = threading.Lock()

    def renew_lease(self, task_id, worker_id, *, lease_seconds=300):
        with self._renew_lock:
            self.renew_count += 1
        return super().renew_lease(task_id, worker_id, lease_seconds=lease_seconds)


class DispatcherTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.queue = TaskQueue(root / "queue.db")
        self.cluster = ClusterScheduler(root / "cluster.db")
        self.cluster.register_worker(
            "heaven",
            "bridge://heaven",
            ["build", "gpu"],
            labels=["worker"],
            capacity=1,
        )
        self.cluster.register_worker(
            "heaven2",
            "bridge://heaven2",
            ["build"],
            labels=["control"],
            capacity=1,
        )

    def tearDown(self):
        self.tmp.cleanup()

    def test_dispatch_routes_completes_and_persists_receipt(self):
        task = self.queue.create_task(
            "build",
            {
                "required_capabilities": ["build"],
                "preferred_labels": ["control"],
                "resources": ["repo:main"],
            },
            task_id="route-1",
        )
        d = QueueClusterDispatcher(self.queue, self.cluster, dispatcher_id="d1")
        result = d.dispatch_once(lambda worker, claimed: {"ran_on": worker["id"]})

        self.assertEqual(result["status"], "completed")
        self.assertEqual(result["worker"], "heaven2")
        stored = self.queue.get_task(task["id"])
        self.assertEqual(stored["state"], "completed")
        self.assertEqual(stored["result"]["worker"], "heaven2")
        self.assertEqual(stored["result"]["result"]["ran_on"], "heaven2")
        self.assertEqual(self.cluster.get_worker("heaven2")["running"], 0)
        self.assertTrue(self.queue.acquire_resource("repo:main", "after:test", lease_seconds=30))

    def test_no_compatible_worker_requeues_and_releases_resources(self):
        task = self.queue.create_task(
            "cuda",
            {
                "required_capabilities": ["cuda"],
                "resources": ["repo:main"],
            },
            task_id="missing-worker",
        )
        d = QueueClusterDispatcher(self.queue, self.cluster, dispatcher_id="d1")
        result = d.dispatch_once(lambda worker, claimed: None)

        self.assertEqual(result["status"], "deferred")
        self.assertEqual(self.queue.get_task(task["id"])["state"], "queued")
        self.assertTrue(self.queue.acquire_resource("repo:main", "after:test", lease_seconds=30))

    def test_cancellation_during_transport_releases_cluster_capacity(self):
        task = self.queue.create_task(
            "build",
            {"required_capabilities": ["build"]},
            task_id="cancel-1",
        )
        d = QueueClusterDispatcher(self.queue, self.cluster, dispatcher_id="d1")

        def transport(worker, claimed):
            self.queue.cancel_task(claimed["id"], "operator cancelled")
            return {"ignored": True}

        result = d.dispatch_once(transport)
        self.assertEqual(result["status"], "cancelled")
        self.assertEqual(self.queue.get_task(task["id"])["state"], "cancelled")
        self.assertEqual(self.cluster.get_worker(result["worker"])["running"], 0)

    def test_long_transport_renews_queue_lease(self):
        root = Path(self.tmp.name)
        queue = CountingQueue(root / "renew.db")
        queue.create_task(
            "build",
            {"required_capabilities": ["build"]},
            task_id="renew-1",
        )
        d = QueueClusterDispatcher(queue, self.cluster, dispatcher_id="renew-dispatcher")

        def transport(worker, claimed):
            time.sleep(0.14)
            return {"ok": True}

        result = d.dispatch_once(
            transport,
            lease_seconds=5,
            lease_renew_interval=0.03,
        )
        self.assertEqual(result["status"], "completed")
        self.assertGreaterEqual(queue.renew_count, 2)

    def test_hard_usage_limit_becomes_capacity_blocked_and_worker_is_released(self):
        task = self.queue.create_task(
            "agent",
            {
                "required_capabilities": ["build"],
            },
            task_id="quota-1",
            capacity_scope="codex",
        )
        d = QueueClusterDispatcher(self.queue, self.cluster, dispatcher_id="d1")

        def transport(worker, claimed):
            raise RuntimeError("You've hit your usage limit. Purchase more credits.")

        result = d.dispatch_once(transport)
        self.assertEqual(result["status"], "capacity_blocked")
        self.assertTrue(result["terminate_agent"])
        self.assertEqual(self.queue.get_task(task["id"])["state"], "capacity_blocked")
        self.assertIsNotNone(self.queue.capacity_status("codex"))
        self.assertEqual(self.cluster.get_worker(result["worker"])["running"], 0)

    def test_two_dispatchers_do_not_double_claim_or_overreserve(self):
        first = self.queue.create_task(
            "build",
            {"required_capabilities": ["build"]},
            task_id="concurrent-1",
        )
        second = self.queue.create_task(
            "build",
            {"required_capabilities": ["build"]},
            task_id="concurrent-2",
        )
        barrier = threading.Barrier(2)
        seen = []
        seen_lock = threading.Lock()
        results = []

        def transport(worker, claimed):
            with seen_lock:
                seen.append((claimed["id"], worker["id"]))
            barrier.wait(timeout=2)
            return {"task": claimed["id"]}

        def run(dispatcher_id):
            d = QueueClusterDispatcher(self.queue, self.cluster, dispatcher_id=dispatcher_id)
            results.append(d.dispatch_once(transport))

        a = threading.Thread(target=run, args=("d-a",))
        b = threading.Thread(target=run, args=("d-b",))
        a.start()
        b.start()
        a.join(timeout=3)
        b.join(timeout=3)

        self.assertFalse(a.is_alive())
        self.assertFalse(b.is_alive())
        self.assertEqual({item[0] for item in seen}, {first["id"], second["id"]})
        self.assertEqual(len({item[1] for item in seen}), 2)
        self.assertTrue(all(item["status"] == "completed" for item in results))
        self.assertEqual(self.cluster.get_worker("heaven")["running"], 0)
        self.assertEqual(self.cluster.get_worker("heaven2")["running"], 0)


if __name__ == "__main__":
    unittest.main()
