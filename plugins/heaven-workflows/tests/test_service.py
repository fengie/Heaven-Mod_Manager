import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from heaven_workflows import HeavenWorkflowPlugin


class FakeControlPlane:
    def __init__(self, failures=None):
        self.calls = []
        self.failures = set(failures or [])

    def invoke(self, capability_name, payload=None, **kwargs):
        self.calls.append((capability_name, dict(payload or {})))
        if capability_name in self.failures:
            return {"status": "error", "capability": capability_name}
        return {"status": "ok", "capability": capability_name, "data": dict(payload or {})}


class WorkflowTests(unittest.TestCase):
    def test_repo_snapshot_composes_git_reads(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        result = plugin.repo_snapshot(r"C:\repo")
        self.assertEqual([c[0] for c in cp.calls], ["git.status", "git.diff", "git.verify_remote_main"])
        self.assertIn("remote_main", result)

    def test_verify_repo_stops_after_failure(self):
        cp = FakeControlPlane({"verification.run"})
        plugin = HeavenWorkflowPlugin(cp)
        result = plugin.verify_repo("repo", kinds=("test", "build"))
        self.assertFalse(result["ok"])
        self.assertEqual([c[0] for c in cp.calls], ["verification.detect", "verification.run"])

    def test_parallel_invoke_preserves_input_order(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        result = plugin.parallel_invoke(
            [
                {"name": "health", "capability": "control.health"},
                {"name": "status", "capability": "git.status", "payload": {"repo": "r"}},
                {"name": "search", "capability": "filesystem.search", "payload": {"path": "r", "pattern": "x"}},
            ],
            max_workers=3,
        )
        self.assertTrue(result["ok"])
        self.assertEqual([x["name"] for x in result["results"]], ["health", "status", "search"])

    def test_parallel_invoke_rejects_raw_execution(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        with self.assertRaises(ValueError):
            plugin.parallel_invoke([{"capability": "execution.run", "payload": {"command": "whoami"}}])

    def test_repo_rejects_traversal(self):
        cp = FakeControlPlane()
        plugin = HeavenWorkflowPlugin(cp)
        with self.assertRaises(ValueError):
            plugin.repo_snapshot("../secret")

    def test_release_rejects_gate_from_wrong_commit(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=({"name":"app.zip","sha256":"b"*64,"size":10},))
        result = plugin.release_verify_gates(plan, ({"name":"gate","sha":"c"*40,"conclusion":"success"},))
        self.assertFalse(result["ok"])
        self.assertEqual("wrong_commit", result["failures"][0]["reason"])

    def test_release_digest_mismatch_blocks_publish(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=({"name":"app.zip","sha256":"b"*64,"size":10},))
        gates = plugin.release_verify_gates(plan, ({"name":"gate","sha":"a"*40,"conclusion":"success"},))
        artifacts = plugin.release_verify_artifacts(plan, ({"name":"app.zip","sha256":"d"*64,"size":10,"version":"1.2.3","channel":"stable"},))
        self.assertFalse(artifacts["ok"])
        with self.assertRaises(ValueError):
            plugin.release_authorize_publish(plan, gates, artifacts, confirmation=f"CONFIRM PUBLISH {plan['plan_id']}")

    def test_release_authorization_and_idempotent_reconcile(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=({"name":"app.zip","sha256":"b"*64,"size":10},))
        gates = plugin.release_verify_gates(plan, ({"name":"gate","sha":"a"*40,"conclusion":"success"},))
        artifacts = plugin.release_verify_artifacts(plan, ({"name":"app.zip","sha256":"b"*64,"size":10,"version":"1.2.3","channel":"stable"},))
        with self.assertRaises(ValueError):
            plugin.release_authorize_publish(plan, gates, artifacts, confirmation="CONFIRM PUBLISH wrong")
        receipt = plugin.release_authorize_publish(plan, gates, artifacts, confirmation=f"CONFIRM PUBLISH {plan['plan_id']}")["receipt"]
        existing = {"source_sha":receipt["source_sha"],"version":receipt["version"],"channel":receipt["channel"],"artifacts":receipt["artifacts"]}
        self.assertEqual("already_published", plugin.release_reconcile_existing(receipt, existing)["action"])
        existing["source_sha"] = "c"*40
        self.assertEqual("conflict", plugin.release_reconcile_existing(receipt, existing)["action"])

    def test_release_cancellation_plan_only_targets_superseded_active_runs(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        result = plugin.release_plan_cancellations("a"*40, ({"id":1,"head_sha":"b"*40,"status":"queued"},{"id":2,"head_sha":"a"*40,"status":"queued"},{"id":3,"head_sha":"c"*40,"status":"completed"}))
        self.assertEqual([1], result["cancel_run_ids"])
        self.assertTrue(result["requires_confirmation"])

    def test_release_authorization_rejects_spoofed_verification_plan(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=({"name":"app.zip","sha256":"b"*64,"size":10},))
        gates = plugin.release_verify_gates(plan, ({"name":"gate","sha":"a"*40,"conclusion":"success"},))
        artifacts = plugin.release_verify_artifacts(plan, ({"name":"app.zip","sha256":"b"*64,"size":10,"version":"1.2.3","channel":"stable"},))
        gates["plan_id"] = "other-plan"
        with self.assertRaises(ValueError):
            plugin.release_authorize_publish(plan, gates, artifacts, confirmation=f"CONFIRM PUBLISH {plan['plan_id']}")

    def test_release_plan_rejects_non_sequences(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        with self.assertRaises(ValueError):
            plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=None, artifacts=({"name":"app.zip"},))
        with self.assertRaises(ValueError):
            plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=None)

    def test_release_verify_artifacts_rejects_malformed_plan_entries(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = {"plan_id":"plan","version":"1.2.3","channel":"stable","artifacts":["not-an-object"]}
        with self.assertRaises(ValueError):
            plugin.release_verify_artifacts(plan, ({"name":"app.zip","sha256":"b"*64,"size":10,"version":"1.2.3","channel":"stable"},))

    def test_gate_does_not_promote_old_success_over_conflicting_same_commit_result(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=({"name":"app.zip"},))
        for outcome in ("failure", "cancelled", "queued"):
            with self.subTest(outcome=outcome):
                result = plugin.release_verify_gates(plan, ({"name":"gate","sha":"a"*40,"conclusion":"success"}, {"name":"gate","sha":"a"*40,"status":outcome}))
                self.assertFalse(result["ok"])

    def test_gate_cannot_be_successful_while_authoritative_status_is_in_progress(self):
        plugin = HeavenWorkflowPlugin(FakeControlPlane())
        plan = plugin.release_plan("repo", candidate_sha="a"*40, version="1.2.3", channel="stable", required_gates=("gate",), artifacts=({"name":"app.zip"},))
        result = plugin.release_verify_gates(plan, ({"name":"gate","sha":"a"*40,"status":"in_progress","conclusion":"success"},))
        self.assertFalse(result["ok"])


if __name__ == "__main__":
    unittest.main()
