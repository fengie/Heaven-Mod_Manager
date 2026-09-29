from __future__ import annotations

import ipaddress
import re
from typing import Any, Mapping, Protocol, Sequence

_SERVICE_RE = re.compile(r"^[A-Za-z0-9_. -]{1,128}$")
_HOST_RE = re.compile(r"^[A-Za-z0-9.-]{1,253}$")


class ControlPlaneLike(Protocol):
    def invoke(
        self,
        capability_name: str,
        payload: Mapping[str, Any] | None = None,
        *,
        req_id: str | None = None,
        permissions: Sequence[str] | None = None,
    ) -> dict[str, Any]:
        ...


class ProcessServicesPlugin:
    """Process, Windows-service, port-health, and dev-server helpers."""

    def __init__(self, control_plane: ControlPlaneLike):
        self.control_plane = control_plane

    @staticmethod
    def _host(value: Any) -> str:
        host = str(value or "").strip()
        try:
            ipaddress.ip_address(host)
            return host
        except ValueError:
            pass
        if not _HOST_RE.fullmatch(host) or ".." in host:
            raise ValueError("invalid host")
        return host

    @staticmethod
    def _service(value: Any) -> str:
        name = str(value or "").strip()
        if not _SERVICE_RE.fullmatch(name):
            raise ValueError("invalid service name")
        return name

    @staticmethod
    def _port(value: Any) -> int:
        if not isinstance(value, int) or not 1 <= value <= 65535:
            raise ValueError("port must be an integer from 1 to 65535")
        return value

    @staticmethod
    def _timeout(value: Any, *, maximum: int = 300, default: int = 30) -> int:
        if value is None:
            return default
        if not isinstance(value, int) or not 1 <= value <= maximum:
            raise ValueError(f"timeout_seconds must be between 1 and {maximum}")
        return value

    def list_processes(self, *, limit: int = 200) -> dict[str, Any]:
        if not isinstance(limit, int) or not 1 <= limit <= 500:
            raise ValueError("limit must be between 1 and 500")
        command = (
            "$ErrorActionPreference='Stop'; "
            "$limit=[int]$env:H_PROC_LIMIT; "
            "Get-Process | Sort-Object CPU -Descending | Select-Object -First $limit "
            "Id,ProcessName,CPU,WorkingSet64,Path | ConvertTo-Json -Depth 3 -Compress"
        )
        return self.control_plane.invoke(
            "execution.run",
            {"shell": "powershell", "command": command, "timeout_seconds": 30, "env": {"H_PROC_LIMIT": str(limit)}},
        )

    def port_probe(self, host: str, port: int, *, timeout_seconds: int = 5) -> dict[str, Any]:
        host = self._host(host)
        port = self._port(port)
        timeout_seconds = self._timeout(timeout_seconds, maximum=30, default=5)
        command = (
            "$ErrorActionPreference='Stop'; "
            "$r=Test-NetConnection -ComputerName $env:H_HOST -Port ([int]$env:H_PORT) "
            "-InformationLevel Detailed -WarningAction SilentlyContinue; "
            "[pscustomobject]@{host=$env:H_HOST;port=[int]$env:H_PORT;"
            "tcp=$r.TcpTestSucceeded;remoteAddress=[string]$r.RemoteAddress} | ConvertTo-Json -Compress"
        )
        return self.control_plane.invoke(
            "execution.run",
            {
                "shell": "powershell",
                "command": command,
                "timeout_seconds": timeout_seconds,
                "env": {"H_HOST": host, "H_PORT": str(port)},
            },
        )

    def wait_port(
        self,
        host: str,
        port: int,
        *,
        timeout_seconds: int = 60,
        interval_ms: int = 500,
    ) -> dict[str, Any]:
        host = self._host(host)
        port = self._port(port)
        timeout_seconds = self._timeout(timeout_seconds, maximum=600, default=60)
        if not isinstance(interval_ms, int) or not 50 <= interval_ms <= 5000:
            raise ValueError("interval_ms must be between 50 and 5000")
        command = (
            "$ErrorActionPreference='Stop'; "
            "$deadline=(Get-Date).AddSeconds([int]$env:H_TIMEOUT); "
            "$ok=$false; do { "
            "$c=New-Object Net.Sockets.TcpClient; try { "
            "$a=$c.BeginConnect($env:H_HOST,[int]$env:H_PORT,$null,$null); "
            "$ok=$a.AsyncWaitHandle.WaitOne([int]$env:H_INTERVAL); "
            "if($ok){$c.EndConnect($a)} } catch {$ok=$false} finally {$c.Dispose()} "
            "if(-not $ok){Start-Sleep -Milliseconds ([int]$env:H_INTERVAL)} "
            "} while((Get-Date) -lt $deadline); "
            "if(-not $ok){throw 'port wait timed out'}; "
            "[pscustomobject]@{host=$env:H_HOST;port=[int]$env:H_PORT;ready=$true} | ConvertTo-Json -Compress"
        )
        return self.control_plane.invoke(
            "execution.run",
            {
                "shell": "powershell",
                "command": command,
                "timeout_seconds": timeout_seconds + 10,
                "env": {
                    "H_HOST": host,
                    "H_PORT": str(port),
                    "H_TIMEOUT": str(timeout_seconds),
                    "H_INTERVAL": str(interval_ms),
                },
            },
        )

    def service_status(self, name: str) -> dict[str, Any]:
        name = self._service(name)
        command = (
            "$ErrorActionPreference='Stop'; "
            "$s=Get-Service -Name $env:H_SERVICE -ErrorAction Stop; "
            "[pscustomobject]@{name=$s.Name;displayName=$s.DisplayName;status=[string]$s.Status;"
            "startType=[string]$s.StartType} | ConvertTo-Json -Compress"
        )
        return self.control_plane.invoke(
            "execution.run",
            {"shell": "powershell", "command": command, "timeout_seconds": 30, "env": {"H_SERVICE": name}},
        )

    def service_action(self, name: str, action: str, *, confirm: bool = False) -> dict[str, Any]:
        name = self._service(name)
        action = str(action or "").lower()
        if action not in {"start", "stop", "restart"}:
            raise ValueError("action must be start, stop, or restart")
        if not confirm:
            raise ValueError("confirm=True is required for service mutations")
        commands = {
            "start": "Start-Service -Name $env:H_SERVICE -ErrorAction Stop",
            "stop": "Stop-Service -Name $env:H_SERVICE -ErrorAction Stop",
            "restart": "Restart-Service -Name $env:H_SERVICE -ErrorAction Stop",
        }
        command = (
            "$ErrorActionPreference='Stop'; "
            + commands[action]
            + "; $s=Get-Service -Name $env:H_SERVICE; "
            "[pscustomobject]@{name=$s.Name;status=[string]$s.Status;action=$env:H_ACTION} "
            "| ConvertTo-Json -Compress"
        )
        return self.control_plane.invoke(
            "execution.run",
            {
                "shell": "powershell",
                "command": command,
                "timeout_seconds": 120,
                "env": {"H_SERVICE": name, "H_ACTION": action},
            },
        )

    def dev_server_start(
        self,
        command: str,
        *,
        cwd: str | None = None,
        shell: str = "powershell",
        idle_timeout_seconds: int = 3600,
        max_runtime_seconds: int = 43200,
    ) -> dict[str, Any]:
        if not isinstance(command, str) or not command.strip() or len(command) > 32768:
            raise ValueError("command must be a non-empty string up to 32768 characters")
        payload: dict[str, Any] = {
            "shell": shell,
            "command": command,
            "idle_timeout_seconds": idle_timeout_seconds,
            "max_runtime_seconds": max_runtime_seconds,
        }
        if cwd is not None:
            payload["cwd"] = cwd
        return self.control_plane.invoke("execution.session.start", payload)

    def dev_server_read(self, session_id: str, *, max_chars: int = 60000) -> dict[str, Any]:
        return self.control_plane.invoke(
            "execution.session.read",
            {"session_id": session_id, "max_chars": max_chars},
        )

    def dev_server_stop(self, session_id: str, *, force: bool = True) -> dict[str, Any]:
        return self.control_plane.invoke(
            "execution.session.stop",
            {"session_id": session_id, "force": force},
        )
