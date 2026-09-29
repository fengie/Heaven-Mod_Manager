import sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_state_store import StateStore

class StoreTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.s=StateStore(self.tmp.name)
    def tearDown(self): self.tmp.cleanup()

    def test_artifact_deduplicates_bytes_by_hash(self):
        a=self.s.publish_artifact("hello",mime="text/plain")
        b=self.s.publish_artifact("hello",mime="text/plain")
        self.assertEqual(a["sha256"],b["sha256"])
        self.assertEqual(self.s.read_artifact(a["id"]),b"hello")

    def test_checkpoint_revision_cas(self):
        x=self.s.put_checkpoint("task",{"step":1})
        self.s.put_checkpoint("task",{"step":2},expected_revision=x["revision"])
        with self.assertRaises(ValueError):
            self.s.put_checkpoint("task",{"step":3},expected_revision=1)

    def test_blocked_metadata_key_refused(self):
        with self.assertRaises(ValueError):
            self.s.publish_artifact(b"x",metadata={"credentials":"not-stored"})

    def test_health_history(self):
        self.s.record_health("heaven",{"cpu":10})
        self.assertEqual(self.s.recent_health("heaven")[0]["metrics"]["cpu"],10)

if __name__=="__main__":unittest.main()
