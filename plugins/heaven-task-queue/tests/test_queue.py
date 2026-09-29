import tempfile
import unittest
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from heaven_task_queue import TaskQueue


class QueueTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.queue = TaskQueue(Path(self.tmp.name) / "queue.db")
        self.queue.register_worker("w1")

    def tearDown(self):
        self.tmp.cleanup()

    def test_dependency_blocks_until_parent_complete(self):
        parent = self.queue.create_task("parent")
        child = self.queue.create_task("child", dependencies=[parent["id"]], priority=10)
        claimed = self.queue.claim_task("w1", lease_seconds=30)
        self.assertEqual(claimed["id"], parent["id"])
        self.queue.complete_task(parent["id"], "w1", {"ok": True})
        claimed2 = self.queue.claim_task("w1", lease_seconds=30)
        self.assertEqual(claimed2["id"], child["id"])

    def test_fail_requeues_until_attempt_limit(self):
        task = self.queue.create_task("x", max_attempts=2)
        self.queue.claim_task("w1", lease_seconds=30)
        state = self.queue.fail_task(task["id"], "w1", "boom", retry=True)
        self.assertEqual(state["state"], "queued")
        self.queue.claim_task("w1", lease_seconds=30)
        state = self.queue.fail_task(task["id"], "w1", "boom2", retry=True)
        self.assertEqual(state["state"], "failed")

    def test_resource_lock_is_exclusive(self):
        self.assertTrue(self.queue.acquire_resource("repo:main", "w1:t1", lease_seconds=30))
        self.assertFalse(self.queue.acquire_resource("repo:main", "w2:t2", lease_seconds=30))
        self.assertTrue(self.queue.release_resource("repo:main", "w1:t1"))
        self.assertTrue(self.queue.acquire_resource("repo:main", "w2:t2", lease_seconds=30))

    def test_cancel_terminal_task_refused(self):
        task = self.queue.create_task("x")
        self.queue.claim_task("w1", lease_seconds=30)
        self.queue.complete_task(task["id"], "w1")
        with self.assertRaises(ValueError):
            self.queue.cancel_task(task["id"])

    def test_payload_limit(self):
        with self.assertRaises(ValueError):
            self.queue.create_task("x", {"data": "x" * 300000})


if __name__ == "__main__":
    unittest.main()
