import sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_cluster import ClusterScheduler

class ClusterTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.s=ClusterScheduler(Path(self.tmp.name)/"c.db")
        self.s.register_worker("heaven","bridge://heaven",["build","gpu"],labels=["local"],capacity=2)
        self.s.register_worker("heaven2","bridge://heaven2",["build"],labels=["control"],capacity=1)
    def tearDown(self): self.tmp.cleanup()

    def test_capability_and_label_routing(self):
        w=self.s.choose_worker(["build"],preferred_labels=["control"])
        self.assertEqual(w["id"],"heaven2")
        self.assertEqual(self.s.choose_worker(["gpu"])["id"],"heaven")

    def test_capacity_reservation(self):
        self.s.reserve_worker("heaven2")
        self.assertEqual(self.s.choose_worker(["build"])["id"],"heaven")

    def test_paused_worker_skipped(self):
        self.s.set_paused("heaven",True)
        with self.assertRaises(RuntimeError): self.s.choose_worker(["gpu"])

    def test_dispatch_releases_capacity(self):
        out=self.s.dispatch({"x":1},["gpu"],lambda worker,task:worker["id"])
        self.assertEqual(out["result"],"heaven")
        self.assertEqual(self.s.get_worker("heaven")["running"],0)

if __name__=="__main__":unittest.main()
