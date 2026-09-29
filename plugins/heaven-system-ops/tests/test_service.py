import sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_system_ops import SystemOpsPlugin

class CP:
    def __init__(self): self.calls=[]
    def invoke(self,n,payload=None,**kw): self.calls.append((n,dict(payload or {}))); return {"status":"ok"}

class SystemOpsTests(unittest.TestCase):
    def test_mutations_require_confirmation(self):
        p=SystemOpsPlugin(CP())
        with self.assertRaises(ValueError): p.package_install("pip","pytest")
        with self.assertRaises(ValueError): p.ssh_exec("host","uptime")
        with self.assertRaises(ValueError): p.docker_action("x","stop")
    def test_host_injection_rejected(self):
        with self.assertRaises(ValueError): SystemOpsPlugin(CP()).dns_lookup("x;whoami")
    def test_remote_command_not_interpolated(self):
        cp=CP(); SystemOpsPlugin(cp).ssh_exec("user@host","echo '; whoami",confirm=True)
        self.assertNotIn("whoami",cp.calls[0][1]["command"])
        self.assertEqual(cp.calls[0][1]["env"]["H_REMOTE"],"echo '; whoami")
    def test_availability_fixed_command(self):
        cp=CP(); SystemOpsPlugin(cp).availability()
        self.assertEqual(cp.calls[0][0],"execution.run")

if __name__=="__main__":unittest.main()
