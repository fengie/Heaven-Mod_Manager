# Heaven Process Services

Reusable process/service/dev-server management over Heaven Control Plane.

Provides process inventory, TCP health/wait checks, Windows service status/start/stop/restart, and persistent dev-server session lifecycle.

Service mutations require `confirm=True`. Hosts, ports, and service names are validated and passed through environment variables rather than interpolated into PowerShell.

Validate with:

```powershell
python .\plugins\heaven-process-services\verify.py
```


## Scheduled maintenance

The process/services plugin can wrap Windows Task Scheduler for bounded recurring jobs without accepting a raw shell command. The caller must configure an exact executable allowlist when constructing the plugin. Job arguments are bounded, credential-like assignments are rejected, environment persistence is not exposed, and every create/enable/disable/run/delete mutation requires explicit confirmation.

Owned task names are `HeavenMaintenance--<owner>--<name>`; the task description contains the exact owner plus a deterministic spec hash. Re-creating an identical job is idempotent, while a different spec with the same owner/name is rejected as an ownership/spec conflict. `run_now` refuses disabled jobs. Missed runs support `skip` or Task Scheduler's `StartWhenAvailable`-backed `run_once` behavior.

Capabilities: `maintenance.create`, `maintenance.list`, `maintenance.enable`, `maintenance.disable`, `maintenance.run_now`, and `maintenance.delete`.
