import sys
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))

from heaven_process_services.scheduler import WindowsMaintenanceScheduler


class CP:
    def __init__(self):
        self.calls=[]
    def invoke(self,name,payload=None,**kwargs):
        self.calls.append((name,dict(payload or {})))
        return {"status":"ok"}


class SchedulerTests(unittest.TestCase):
    EXE=r"C:\Heaven\bin\maintenance-runner.exe"

    def scheduler(self):
        cp=CP()
        return WindowsMaintenanceScheduler(cp,(self.EXE,)),cp

    def test_create_requires_confirmation(self):
        scheduler,_=self.scheduler()
        with self.assertRaises(ValueError):
            scheduler.create_job("agent","cleanup",self.EXE)

    def test_create_rejects_nonallowlisted_executable(self):
        scheduler,_=self.scheduler()
        with self.assertRaises(ValueError):
            scheduler.create_job("agent","cleanup",r"C:\Windows\System32\cmd.exe",confirm=True)

    def test_create_uses_env_not_command_interpolation_and_exact_owner(self):
        scheduler,cp=self.scheduler()
        result=scheduler.create_job(
            "agent","cleanup",self.EXE,
            args=("--mode","cleanup","--credential-handle","handle:cleanup-1"),
            interval_minutes=30,
            missed_run_policy="run_once",
            confirm=True,
        )
        payload=cp.calls[0][1]
        self.assertEqual("execution.run",cp.calls[0][0])
        self.assertNotIn(self.EXE,payload["command"])
        self.assertEqual("HeavenMaintenance--agent--cleanup",payload["env"]["H_TASK_NAME"])
        self.assertTrue(payload["env"]["H_DESCRIPTION"].startswith("heaven-maintenance/v1 owner=agent "))
        self.assertEqual(result["task_name"],payload["env"]["H_TASK_NAME"])

    def test_secret_assignment_in_args_rejected(self):
        scheduler,_=self.scheduler()
        with self.assertRaises(ValueError):
            scheduler.create_job(
                "agent","cleanup",self.EXE,args=("token=raw-secret",),confirm=True
            )

    def test_mutations_require_confirmation(self):
        scheduler,_=self.scheduler()
        for method in (scheduler.enable_job,scheduler.disable_job,scheduler.run_now,scheduler.delete_job):
            with self.assertRaises(ValueError):
                method("agent","cleanup")

    def test_run_now_checks_disabled_state_before_start(self):
        scheduler,cp=self.scheduler()
        scheduler.run_now("agent","cleanup",confirm=True)
        command=cp.calls[0][1]["command"]
        self.assertIn("disabled scheduled job cannot run",command)
        self.assertIn("StartsWith($env:H_OWNER_MARKER)",command)

    def test_list_is_namespaced(self):
        scheduler,cp=self.scheduler()
        scheduler.list_jobs(owner="agent")
        payload=cp.calls[0][1]
        self.assertEqual("heaven-maintenance/v1 owner=agent ",payload["env"]["H_OWNER_MARKER"])
        self.assertIn("HeavenMaintenance--*",payload["command"])


if __name__=="__main__":
    unittest.main()
