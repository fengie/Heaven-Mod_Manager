import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))

from heaven_workflows.rollback import RollbackCoordinator


class Store:
    def __init__(self):
        self.values={}
    def get_checkpoint(self,key):
        value=self.values.get(key)
        if value is None:
            return None
        return {"key":key,"payload":dict(value["payload"]),"revision":value["revision"]}
    def put_checkpoint(self,key,payload,*,expected_revision=None):
        current=self.values.get(key)
        current_revision=None if current is None else current["revision"]
        compare_revision=0 if current_revision is None else current_revision
        if expected_revision is not None and compare_revision != expected_revision:
            raise ValueError("revision conflict")
        revision=1 if current_revision is None else current_revision+1
        self.values[key]={"payload":dict(payload),"revision":revision}
        return {"key":key,"revision":revision}


class Invoker:
    def __init__(self,fail=None):
        self.calls=[]
        self.fail=set(fail or [])
    def invoke(self,name,payload=None,**kwargs):
        self.calls.append((name,dict(payload or {})))
        return {"status":"error" if name in self.fail else "ok"}


class RacingStore(Store):
    def __init__(self):
        super().__init__()
        self.raced=False
    def put_checkpoint(self,key,payload,*,expected_revision=None):
        if key=="rollback:change-1" and expected_revision==0 and not self.raced:
            self.raced=True
            self.values[key]={"payload":{"plan_id":"other-plan"},"revision":1}
        return super().put_checkpoint(key,payload,expected_revision=expected_revision)


class RollbackTests(unittest.TestCase):
    def plan(self,invoker=None):
        store=Store()
        invoker=invoker or Invoker()
        coordinator=RollbackCoordinator(store,invoker)
        prepared=coordinator.prepare_change(
            "change-1",
            pre_state_id="pre-sha",
            expected_post_state_id="post-sha",
            preconditions={"branch":"main"},
            rollback_steps=(
                {"step_id":"restore-file","capability":"filesystem.write","payload":{"path":"a","content":"old"}},
                {"step_id":"restore-ref","capability":"git.restore_ref","payload":{"ref":"main","sha":"pre-sha"}},
            ),
        )
        return coordinator,store,invoker,prepared

    def test_commit_receipt_contains_exact_state_ids(self):
        coordinator,_,_,_=self.plan()
        receipt=coordinator.commit_change("change-1",post_state_id="post-sha")
        self.assertEqual("pre-sha",receipt["pre_state_id"])
        self.assertEqual("post-sha",receipt["post_state_id"])
        self.assertTrue(receipt["receipt_id"])

    def test_rollback_runs_only_declared_steps_in_reverse_order(self):
        coordinator,_,invoker,_=self.plan()
        coordinator.commit_change("change-1",post_state_id="post-sha")
        result=coordinator.rollback_change("change-1",reason="verification failed",confirm=True)
        self.assertTrue(result["ok"])
        self.assertEqual(["git.restore_ref","filesystem.write"],[call[0] for call in invoker.calls])

    def test_rollback_is_idempotent(self):
        coordinator,_,invoker,_=self.plan()
        coordinator.rollback_change("change-1",reason="abort",confirm=True)
        count=len(invoker.calls)
        result=coordinator.rollback_change("change-1",reason="abort",confirm=True)
        self.assertTrue(result["idempotent"])
        self.assertEqual(count,len(invoker.calls))

    def test_partial_failure_retry_skips_completed_inverse_steps(self):
        invoker=Invoker({"filesystem.write"})
        coordinator,_,_,_=self.plan(invoker)
        first=coordinator.rollback_change("change-1",reason="abort",confirm=True)
        self.assertFalse(first["ok"])
        self.assertEqual(["git.restore_ref","filesystem.write"],[call[0] for call in invoker.calls])
        invoker.fail.clear()
        second=coordinator.rollback_change("change-1",reason="retry",confirm=True)
        self.assertTrue(second["ok"])
        self.assertEqual(
            ["git.restore_ref","filesystem.write","filesystem.write"],
            [call[0] for call in invoker.calls],
        )

    def test_raw_execution_inverse_is_rejected(self):
        with self.assertRaises(ValueError):
            RollbackCoordinator(Store(),Invoker()).prepare_change(
                "change-1",
                pre_state_id="pre",
                rollback_steps=({"step_id":"bad","capability":"execution.run","payload":{"command":"whoami"}},),
            )

    def test_credential_bearing_persisted_payload_is_rejected(self):
        with self.assertRaises(ValueError):
            RollbackCoordinator(Store(),Invoker()).prepare_change(
                "change-1",
                pre_state_id="pre",
                rollback_steps=({"step_id":"bad","capability":"service.restart","payload":{"token":"raw-secret"}},),
            )

    def test_credential_like_value_under_generic_key_is_rejected(self):
        token="ghp_" + ("a"*36)
        with self.assertRaises(ValueError):
            RollbackCoordinator(Store(),Invoker()).prepare_change(
                "change-1",
                pre_state_id="pre",
                rollback_steps=({"step_id":"bad","capability":"service.restart","payload":{"value":token}},),
            )

    def test_prepare_change_cannot_overwrite_concurrent_owner(self):
        store=RacingStore()
        coordinator=RollbackCoordinator(store,Invoker())
        with self.assertRaises(ValueError):
            coordinator.prepare_change(
                "change-1",
                pre_state_id="pre",
                rollback_steps=({"step_id":"safe","capability":"service.restart","payload":{"service":"demo"}},),
            )
        self.assertEqual("other-plan",store.values["rollback:change-1"]["payload"]["plan_id"])

    def test_opaque_handle_is_allowed(self):
        RollbackCoordinator(Store(),Invoker()).prepare_change(
            "change-1",
            pre_state_id="pre",
            rollback_steps=({"step_id":"ok","capability":"service.restart","payload":{"credential_handle":"handle:svc-1"}},),
        )

    def test_post_state_mismatch_blocks_commit(self):
        coordinator,_,_,_=self.plan()
        with self.assertRaises(ValueError):
            coordinator.commit_change("change-1",post_state_id="unexpected")

    def test_partial_rollback_cannot_be_committed_and_can_resume(self):
        invoker=Invoker({"filesystem.write"})
        coordinator,store,_,_=self.plan(invoker)
        first=coordinator.rollback_change("change-1",reason="abort",confirm=True)
        self.assertFalse(first["ok"])
        with self.assertRaises(ValueError):
            coordinator.commit_change("change-1",post_state_id="post-sha")
        checkpoint=store.values["rollback:change-1"]
        checkpoint["payload"]["status"]="rolling_back"
        invoker.fail.clear()
        resumed=coordinator.rollback_change("change-1",reason="resume after crash",confirm=True)
        self.assertTrue(resumed["ok"])


if __name__=="__main__":
    unittest.main()
