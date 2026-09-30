import copy
import json
import sys
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from heaven_workflows.github_evidence import collect_commit_runs, validate_commit_evidence, EvidenceUnavailable, PAGE_SIZE, MAX_PAGES
from heaven_workflows import HeavenWorkflowPlugin

SHA = "a" * 40
NOW = datetime(2026, 9, 30, 19, tzinfo=timezone.utc)


def run(identity=1, workflow=10, conclusion="success", status="completed", **extra):
    return {"id": identity, "workflow_id": workflow, "name": "Windows Release Gate", "head_sha": SHA,
            "event": "push", "head_branch": "main", "created_at": (NOW - timedelta(seconds=1000-identity)).isoformat(),
            "run_attempt": 1, "status": status, "conclusion": conclusion, **extra}


class EvidenceTests(unittest.TestCase):
    def packet(self, rows=None, **options):
        rows = rows if rows is not None else [run()]
        return collect_commit_runs("fengie/mhw-mods", SHA, lambda path: {"total_count": len(rows), "workflow_runs": rows}, now=NOW, **options)

    def test_main_push_query_is_exact_scoped_and_does_not_filter_failed_statuses(self):
        paths = []
        packet = collect_commit_runs("fengie/mhw-mods", SHA, lambda path: paths.append(path) or {"total_count": 1, "workflow_runs": [run()]}, now=NOW)
        query = parse_qs(urlsplit(paths[0]).query)
        self.assertEqual(query["head_sha"], [SHA])
        self.assertEqual(query["event"], ["push"])
        self.assertEqual(query["branch"], ["main"])
        self.assertNotIn("status", query)
        self.assertEqual(validate_commit_evidence(packet, "fengie/mhw-mods", SHA, now=NOW)[0]["id"], 1)

    def test_wrong_source_event_branch_and_malformed_response_are_rejected(self):
        for change in ({"head_sha": "b"*40}, {"event": "pull_request"}, {"head_branch": "other"}, {"workflow_id": False}, {"status": "passed"}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                self.packet([run(**change)])
        for response in ({}, {"total_count": True, "workflow_runs": []}, {"total_count": 1, "workflow_runs": ["invalid"]}):
            with self.subTest(response=response), self.assertRaises(ValueError):
                collect_commit_runs("fengie/mhw-mods", SHA, lambda path: response, now=NOW)

    def test_scope_validation_blocks_path_or_query_injection(self):
        for repo, sha, event, branch in [("../private", SHA, "push", "main"), ("fengie/mhw-mods", "main", "push", "main"), ("fengie/mhw-mods", SHA, "unknown", "main"), ("fengie/mhw-mods", SHA, "push", "main\nsecret")]:
            with self.assertRaises(ValueError):
                collect_commit_runs(repo, sha, lambda path: {}, event=event, branch=branch, now=NOW)

    def test_provider_errors_are_not_empty_success_and_do_not_relay_credentials(self):
        def broken(path):
            raise RuntimeError("secret-provider-token")
        with self.assertRaises(EvidenceUnavailable) as caught:
            collect_commit_runs("fengie/mhw-mods", SHA, broken, now=NOW)
        self.assertNotIn("secret-provider-token", str(caught.exception))
        plugin = HeavenWorkflowPlugin(object())
        with self.assertRaisesRegex(ValueError, "not configured"):
            plugin.release_fetch_gates("fengie/mhw-mods", SHA)

    def test_multi_page_proof_retains_newest_failure_instead_of_old_success(self):
        rows = [run(i, i+10) for i in range(1, PAGE_SIZE+1)]
        rows.append(run(PAGE_SIZE+1, 11, conclusion="failure"))
        calls = []
        def reader(path):
            page = int(parse_qs(urlsplit(path).query)["page"][0])
            calls.append(page)
            return {"total_count": len(rows), "workflow_runs": rows[(page-1)*PAGE_SIZE:page*PAGE_SIZE]}
        packet = collect_commit_runs("fengie/mhw-mods", SHA, reader, now=NOW)
        self.assertEqual(calls, [1, 2])
        self.assertTrue(packet["complete"])
        latest = next(row for row in packet["runs"] if row["workflow_id"] == 11)
        self.assertEqual(latest["conclusion"], "failure")
        self.assertEqual(packet["fetched_count"], PAGE_SIZE+1)

    def test_latest_pending_and_success_after_previous_failure_are_honest(self):
        for status, conclusion in [("queued", None), ("completed", "failure"), ("completed", "success")]:
            packet = self.packet([run(1, conclusion="failure"), run(2, conclusion=conclusion, status=status)])
            self.assertEqual(len(packet["runs"]), 1)
            self.assertEqual(packet["runs"][0]["status"], status)
            self.assertEqual(packet["runs"][0]["conclusion"], conclusion)

    def test_page_limit_changed_counts_and_short_pages_are_incomplete(self):
        def many(path):
            page = int(parse_qs(urlsplit(path).query)["page"][0])
            return {"total_count": 500, "workflow_runs": [run((page-1)*PAGE_SIZE+i, i) for i in range(1, PAGE_SIZE+1)]}
        packet = collect_commit_runs("fengie/mhw-mods", SHA, many, now=NOW)
        self.assertEqual(packet["fetched_count"], PAGE_SIZE*MAX_PAGES)
        self.assertEqual(packet["incomplete_reason"], "page_limit")
        with self.assertRaises(EvidenceUnavailable):
            validate_commit_evidence(packet, "fengie/mhw-mods", SHA, now=NOW)
        short = collect_commit_runs("fengie/mhw-mods", SHA, lambda path: {"total_count": 2, "workflow_runs": [run()]}, now=NOW)
        self.assertFalse(short["complete"])
        calls = []
        def moving(path):
            calls.append(path)
            return {"total_count": 51 if len(calls)==1 else 52, "workflow_runs": [run(i,i) for i in range(1,51)] if len(calls)==1 else [run(51,51),run(52,52)]}
        changed = collect_commit_runs("fengie/mhw-mods", SHA, moving, now=NOW)
        self.assertEqual(changed["incomplete_reason"], "snapshot_changed")

    def test_ambiguous_order_and_changing_same_run_fail_closed(self):
        packet = self.packet([run(1), run(2, created_at=run(1)["created_at"], conclusion="failure")])
        self.assertEqual(packet["incomplete_reason"], "ambiguous_run_order")
        changed = self.packet([run(1), run(1, run_attempt=2)])
        self.assertFalse(changed["complete"])

    def test_expired_cross_scope_tampered_counts_and_duplicate_workflows_are_rejected(self):
        packet = self.packet()
        with self.assertRaises(EvidenceUnavailable):
            validate_commit_evidence(packet, "fengie/mhw-mods", SHA, now=NOW+timedelta(seconds=120))
        with self.assertRaises(EvidenceUnavailable):
            validate_commit_evidence(packet, "fengie/mhw-mods", SHA, event="pull_request", now=NOW)
        for field, value in [("fetched_count", 0), ("total_count", True), ("runs", packet["runs"]*2)]:
            changed = copy.deepcopy(packet)
            changed[field] = value
            with self.assertRaises(EvidenceUnavailable):
                validate_commit_evidence(changed, "fengie/mhw-mods", SHA, now=NOW)

    def test_raw_provider_secrets_and_untrusted_links_are_not_serialized(self):
        packet = self.packet([run(token="secret-value", html_url="https://evil.example/token=secret-value", repository={"token":"secret-value"})])
        self.assertNotIn("secret-value", json.dumps(packet))
        self.assertEqual(packet["runs"][0]["url"], "https://github.com/fengie/mhw-mods/actions/runs/1")

    def test_oversized_response_and_empty_required_gates_do_not_pass(self):
        with self.assertRaises(EvidenceUnavailable):
            collect_commit_runs("fengie/mhw-mods", SHA, lambda path: {"total_count": 0, "workflow_runs": [], "body": "x"*2_097_152}, now=NOW)
        plugin = HeavenWorkflowPlugin(object(), github_reader=lambda path: {"total_count": 0, "workflow_runs": []})
        plan = plugin.release_plan("fengie/mhw-mods", candidate_sha=SHA, version="8.8.42", channel="stable", required_gates=["Windows Release Gate"], artifacts=[{"name":"app.zip"}])
        self.assertFalse(plugin.release_verify_github_gates(plan, plugin.release_fetch_gates("fengie/mhw-mods", SHA))["ok"])

    def test_publication_rechecks_snapshot_freshness_and_status_instead_of_cached_ok(self):
        plugin = HeavenWorkflowPlugin(object(), github_reader=lambda path: {"total_count": 1, "workflow_runs": [run()]})
        plan = plugin.release_plan("fengie/mhw-mods", candidate_sha=SHA, version="8.8.42", channel="stable", required_gates=["Windows Release Gate"], artifacts=[{"name":"app.zip","sha256":"b"*64,"size":1}])
        gates = plugin.release_verify_github_gates(plan, plugin.release_fetch_gates("fengie/mhw-mods", SHA))
        artifacts = plugin.release_verify_artifacts(plan, [{"name":"app.zip","sha256":"b"*64,"size":1,"version":"8.8.42","channel":"stable"}])
        confirmation = f"CONFIRM PUBLISH {plan['plan_id']}"
        self.assertTrue(plugin.release_authorize_publish(plan, gates, artifacts, confirmation=confirmation)["authorized"])
        changed = copy.deepcopy(gates)
        changed["github_evidence"]["runs"][0]["conclusion"] = "failure"
        with self.assertRaises(ValueError):
            plugin.release_authorize_publish(plan, changed, artifacts, confirmation=confirmation)
        changed = copy.deepcopy(gates)
        changed["github_evidence"]["observed_at"] = (NOW-timedelta(days=365)).isoformat()
        changed["github_evidence"]["expires_at"] = (NOW-timedelta(days=365)+timedelta(seconds=120)).isoformat()
        with self.assertRaises(ValueError):
            plugin.release_authorize_publish(plan, changed, artifacts, confirmation=confirmation)

    def test_plugin_uses_authorized_reader_and_binds_complete_snapshot_to_plan(self):
        def reader(path):
            return {"total_count": 1, "workflow_runs": [run()]}
        plugin = HeavenWorkflowPlugin(object(), github_reader=reader)
        plan = plugin.release_plan("fengie/mhw-mods", candidate_sha=SHA, version="8.8.42", channel="stable", required_gates=["Windows Release Gate"], artifacts=[{"name":"app.zip"}])
        packet = plugin.release_fetch_gates("fengie/mhw-mods", SHA)
        self.assertTrue(plugin.release_verify_github_gates(plan, packet)["ok"])
        plan["candidate_sha"] = "b"*40
        with self.assertRaises(EvidenceUnavailable):
            plugin.release_verify_github_gates(plan, packet)


if __name__ == "__main__":
    unittest.main()
