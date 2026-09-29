import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))

from heaven_process_services import ProcessServicesPlugin


class FakeCP:
    def __init__(self): self.calls=[]
    def invoke(self,name,payload=None,**kwargs):
        self.calls.append((name,dict(payload or {})))
        return {"status":"ok","capability":name}


class ProcessServiceTests(unittest.TestCase):
    def test_probe_uses_env_not_interpolation(self):
        cp=FakeCP(); p=ProcessServicesPlugin(cp)
        p.port_probe("127.0.0.1",7331)
        payload=cp.calls[0][1]
        self.assertNotIn("127.0.0.1",payload["command"])
        self.assertEqual(payload["env"]["H_PORT"],"7331")

    def test_service_mutation_requires_confirmation(self):
        with self.assertRaises(ValueError):
            ProcessServicesPlugin(FakeCP()).service_action("Spooler","restart")

    def test_bad_host_rejected(self):
        with self.assertRaises(ValueError):
            ProcessServicesPlugin(FakeCP()).port_probe("x;whoami",80)

    def test_dev_server_uses_session(self):
        cp=FakeCP()
        ProcessServicesPlugin(cp).dev_server_start("python -m http.server")
        self.assertEqual(cp.calls[0][0],"execution.session.start")


if __name__=="__main__": unittest.main()
