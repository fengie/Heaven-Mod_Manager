# Heaven Process Services

Reusable process/service/dev-server management over Heaven Control Plane.

Provides process inventory, TCP health/wait checks, Windows service status/start/stop/restart, and persistent dev-server session lifecycle.

Service mutations require `confirm=True`. Hosts, ports, and service names are validated and passed through environment variables rather than interpolated into PowerShell.

Validate with:

```powershell
python .\plugins\heaven-process-services\verify.py
```
