import tempfile
import unittest
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from heaven_task_queue import DEFAULT_CAPACITY_NOTICE, TaskQueue, is_hard_usage_limit


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

    def test_task_identifier_rejects_sql_wildcards(self):
        with self.assertRaises(ValueError):
            self.queue.create_task("x", task_id="bad%id")

    def test_cancel_releases_only_exact_leased_task_lock(self):
        task = self.queue.create_task("x", task_id="task-1")
        self.queue.claim_task("w1", lease_seconds=30)
        self.assertTrue(self.queue.acquire_resource("repo:one", "w1:task-1", lease_seconds=30))
        self.assertTrue(self.queue.acquire_resource("repo:two", "other:task-2", lease_seconds=30))
        self.queue.cancel_task("task-1")
        self.assertTrue(self.queue.acquire_resource("repo:one", "w2:new", lease_seconds=30))
        self.assertFalse(self.queue.acquire_resource("repo:two", "w2:new", lease_seconds=30))


    def test_hard_usage_limit_detection_matches_provider_quota_not_transient_rate_limit(self):
        message = (
            "You've hit your usage limit. Upgrade to Pro, visit settings to purchase more credits "
            "or try again at Oct 4th, 2026 8:18 AM."
        )
        self.assertTrue(is_hard_usage_limit(message))
        self.assertTrue(is_hard_usage_limit("insufficient_quota: billing credits exhausted"))
        self.assertFalse(is_hard_usage_limit("429 rate limit exceeded; retry in 20 seconds"))

    def test_hard_usage_limit_becomes_terminal_notice_and_terminates_agent(self):
        task = self.queue.create_task("agent", task_id="quota-1", capacity_scope="codex")
        self.queue.claim_task("w1", lease_seconds=30)
        self.assertTrue(self.queue.acquire_resource("repo:main", "w1:quota-1", lease_seconds=30))
        killed = []
        result = self.queue.handle_agent_failure(
            task["id"],
            "w1",
            "You've hit your usage limit. Upgrade to Pro or purchase more credits.",
            terminate=lambda: killed.append(True),
        )
        self.assertTrue(result["hard_usage_limit"])
        self.assertTrue(result["terminate_agent"])
        self.assertTrue(result["terminated"])
        self.assertEqual(killed, [True])
        self.assertEqual(result["notice"], DEFAULT_CAPACITY_NOTICE)
        self.assertEqual(result["task"]["state"], "capacity_blocked")
        self.assertIsNone(result["task"]["lease_owner"])
        self.assertEqual(result["task"]["result"]["terminal_reason"], "hard_usage_limit")
        self.assertEqual(self.queue.capacity_status("codex")["notice"], DEFAULT_CAPACITY_NOTICE)
        self.assertTrue(self.queue.acquire_resource("repo:main", "w2:new", lease_seconds=30))

    def test_capacity_block_prevents_same_mode_respawn_until_cleared(self):
        first = self.queue.create_task("agent", task_id="first", capacity_scope="codex", priority=100)
        self.queue.claim_task("w1", lease_seconds=30)
        self.queue.handle_agent_failure(
            first["id"],
            "w1",
            "You've hit your usage limit. Purchase more credits or try again later.",
        )
        blocked = self.queue.create_task("agent", task_id="blocked", capacity_scope="codex", priority=100)
        healthy = self.queue.create_task("agent", task_id="healthy", capacity_scope="chat", priority=10)
        claimed = self.queue.claim_task("w1", lease_seconds=30)
        self.assertEqual(claimed["id"], healthy["id"])
        self.queue.complete_task(healthy["id"], "w1")
        self.assertIsNone(self.queue.claim_task("w1", lease_seconds=30))

        self.assertTrue(self.queue.clear_capacity("codex"))
        claimed_after_clear = self.queue.claim_task("w1", lease_seconds=30)
        self.assertEqual(claimed_after_clear["id"], blocked["id"])

    def test_capacity_blocked_history_is_not_resurrected_when_scope_clears(self):
        task = self.queue.create_task("agent", task_id="history", capacity_scope="codex")
        self.queue.claim_task("w1", lease_seconds=30)
        self.queue.handle_agent_failure(
            task["id"],
            "w1",
            "You've hit your usage limit. Upgrade or purchase more credits.",
        )
        self.queue.clear_capacity("codex")
        self.assertEqual(self.queue.get_task(task["id"])["state"], "capacity_blocked")

    def test_non_quota_failure_keeps_normal_retry_behavior(self):
        task = self.queue.create_task("agent", task_id="ordinary", capacity_scope="chat")
        self.queue.claim_task("w1", lease_seconds=30)
        result = self.queue.handle_agent_failure(task["id"], "w1", "network stream failed", retry=True)
        self.assertFalse(result["hard_usage_limit"])
        self.assertFalse(result["terminate_agent"])
        self.assertEqual(result["task"]["state"], "queued")
        self.assertIsNone(self.queue.capacity_status("chat"))

    def test_termination_callback_failure_does_not_make_quota_task_healthy(self):
        task = self.queue.create_task("agent", task_id="kill-error", capacity_scope="codex")
        self.queue.claim_task("w1", lease_seconds=30)

        def fail_kill():
            raise RuntimeError("kill failed")

        result = self.queue.handle_agent_failure(
            task["id"],
            "w1",
            "You've hit your usage limit. Purchase more credits.",
            terminate=fail_kill,
        )
        self.assertTrue(result["terminate_agent"])
        self.assertFalse(result["terminated"])
        self.assertIn("kill failed", result["termination_error"])
        self.assertEqual(self.queue.get_task(task["id"])["state"], "capacity_blocked")


if __name__ == "__main__":
    unittest.main()
