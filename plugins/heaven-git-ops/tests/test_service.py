import sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_git_ops import GitOpsPlugin

class CP:
    def __init__(self): self.calls=[]
    def invoke(self,n,payload=None,**kw): self.calls.append((n,dict(payload or {}))); return {"status":"ok"}

class GitOpsTests(unittest.TestCase):
    def test_branch_injection_rejected(self):
        with self.assertRaises(ValueError): GitOpsPlugin(CP()).push("r","x;whoami",confirm=True)
    def test_push_requires_confirmation(self):
        with self.assertRaises(ValueError): GitOpsPlugin(CP()).push("r","feature/x")
    def test_commit_message_not_interpolated(self):
        cp=CP(); GitOpsPlugin(cp).commit("r","hi'; whoami")
        self.assertNotIn("whoami",cp.calls[0][1]["command"])
        self.assertEqual(cp.calls[0][1]["env"]["H_MESSAGE"],"hi'; whoami")
    def test_main_delete_refused(self):
        with self.assertRaises(ValueError): GitOpsPlugin(CP()).delete_branch("r","main",confirm=True)
    def test_integrate_requires_confirmation(self):
        with self.assertRaises(ValueError): GitOpsPlugin(CP()).integrate_task_to_main("r","agent/x")

if __name__=="__main__":unittest.main()
