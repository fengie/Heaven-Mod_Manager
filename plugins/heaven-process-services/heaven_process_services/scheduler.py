from __future__ import annotations

import hashlib
import json
import ntpath
import re
import subprocess
from typing import Any, Mapping, Protocol, Sequence

_NAME_RE = re.compile(r"^[A-Za-z0-9_.-]{1,64}$")
_SECRET_ASSIGN_RE = re.compile(
    r"(?i)(?:password|passwd|secret|token|api[-_]?key|private[-_]?key)\s*[=:]"
)


class ControlPlaneLike(Protocol):
    def invoke(
        self,
        capability_name: str,
        payload: Mapping[str, Any] | None = None,
        **kwargs: Any,
    ) -> Mapping[str, Any]:
        ...


class WindowsMaintenanceScheduler:
    """Owned Task Scheduler wrapper restricted to explicitly allowlisted executables."""

    PREFIX = "HeavenMaintenance--"
    DESCRIPTION_PREFIX = "heaven-maintenance/v1"

    def __init__(self, control_plane: ControlPlaneLike, allowed_executables: Sequence[str] = ()):
        self.control_plane = control_plane
        if isinstance(allowed_executables, (str, bytes)) or not isinstance(allowed_executables, Sequence):
            raise ValueError("allowed_executables must be a sequence")
        normalized = set()
        for value in allowed_executables:
            normalized.add(self._executable(value))
        self.allowed_executables = frozenset(normalized)

    @staticmethod
    def _name(value: Any, label: str) -> str:
        if not isinstance(value, str) or not _NAME_RE.fullmatch(value):
            raise ValueError(f"{label} must match [A-Za-z0-9_.-] and be 1..64 characters")
        return value

    @staticmethod
    def _executable(value: Any) -> str:
        if not isinstance(value, str) or not value or len(value) > 2048 or "\x00" in value:
            raise ValueError("invalid executable path")
        normalized = ntpath.normcase(ntpath.normpath(value))
        if not ntpath.isabs(normalized):
            raise ValueError("scheduled executable path must be absolute")
        return normalized

    @staticmethod
    def _cwd(value: Any) -> str | None:
        if value is None:
            return None
        if not isinstance(value, str) or not value or len(value) > 2048 or "\x00" in value:
            raise ValueError("invalid working directory")
        normalized = ntpath.normpath(value)
        if not ntpath.isabs(normalized):
            raise ValueError("scheduled working directory must be absolute")
        return normalized

    @staticmethod
    def _args(value: Any) -> list[str]:
        if value is None:
            return []
        if isinstance(value, (str, bytes)) or not isinstance(value, Sequence) or len(value) > 64:
            raise ValueError("args must be a sequence of at most 64 strings")
        output = []
        total = 0
        for item in value:
            if not isinstance(item, str) or "\x00" in item or len(item) > 2048:
                raise ValueError("each scheduled argument must be a string up to 2048 characters")
            if _SECRET_ASSIGN_RE.search(item):
                raise ValueError("credential values must not be embedded in scheduled arguments; use opaque handle arguments")
            total += len(item)
            if total > 8192:
                raise ValueError("scheduled arguments exceed 8192 characters")
            output.append(item)
        return output

    @classmethod
    def task_name(cls, owner: str, name: str) -> str:
        return f"{cls.PREFIX}{cls._name(owner, 'owner')}--{cls._name(name, 'name')}"

    @classmethod
    def owner_marker(cls, owner: str) -> str:
        return f"{cls.DESCRIPTION_PREFIX} owner={cls._name(owner, 'owner')} "

    @staticmethod
    def _confirm(confirm: bool) -> None:
        if confirm is not True:
            raise ValueError("confirm=True is required for scheduled-maintenance mutations")

    def _owned_env(self, owner: str, name: str) -> dict[str, str]:
        owner = self._name(owner, "owner")
        name = self._name(name, "name")
        return {
            "H_TASK_NAME": self.task_name(owner, name),
            "H_OWNER_MARKER": self.owner_marker(owner),
        }

    def create_job(
        self,
        owner: str,
        name: str,
        executable: str,
        *,
        args: Sequence[str] = (),
        working_directory: str | None = None,
        interval_minutes: int = 60,
        missed_run_policy: str = "skip",
        max_runtime_minutes: int = 60,
        enabled: bool = True,
        confirm: bool = False,
    ) -> Mapping[str, Any]:
        self._confirm(confirm)
        owner = self._name(owner, "owner")
        name = self._name(name, "name")
        executable = self._executable(executable)
        if executable not in self.allowed_executables:
            raise ValueError("scheduled executable is not allowlisted")
        args = self._args(args)
        working_directory = self._cwd(working_directory)
        if not isinstance(interval_minutes, int) or not 5 <= interval_minutes <= 1439:
            raise ValueError("interval_minutes must be 5..1439")
        if missed_run_policy not in {"skip", "run_once"}:
            raise ValueError("missed_run_policy must be skip or run_once")
        if not isinstance(max_runtime_minutes, int) or not 1 <= max_runtime_minutes <= 1440:
            raise ValueError("max_runtime_minutes must be 1..1440")
        if not isinstance(enabled, bool):
            raise ValueError("enabled must be boolean")
        task_name = self.task_name(owner, name)
        argument_line = subprocess.list2cmdline(args)
        spec = {
            "owner": owner,
            "name": name,
            "executable": executable,
            "args": args,
            "working_directory": working_directory,
            "interval_minutes": interval_minutes,
            "missed_run_policy": missed_run_policy,
            "max_runtime_minutes": max_runtime_minutes,
            "enabled": enabled,
        }
        spec_hash = hashlib.sha256(
            json.dumps(spec, sort_keys=True, separators=(",", ":")).encode("utf-8")
        ).hexdigest()
        description = f"{self.owner_marker(owner)}spec={spec_hash}"
        command = (
            "$ErrorActionPreference='Stop'; "
            "$existing=Get-ScheduledTask -TaskName $env:H_TASK_NAME -ErrorAction SilentlyContinue; "
            "if($existing){ "
            "if($existing.Description -eq $env:H_DESCRIPTION){ "
            "[pscustomobject]@{taskName=$env:H_TASK_NAME;created=$false;idempotent=$true;state=[string]$existing.State}|ConvertTo-Json -Compress; exit 0 }; "
            "throw 'scheduled-maintenance ownership/spec conflict' }; "
            "if([string]::IsNullOrWhiteSpace($env:H_CWD)){"
            "$action=New-ScheduledTaskAction -Execute $env:H_EXEC -Argument $env:H_ARGS"
            "}else{"
            "$action=New-ScheduledTaskAction -Execute $env:H_EXEC -Argument $env:H_ARGS -WorkingDirectory $env:H_CWD"
            "}; "
            "$trigger=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1)"
            " -RepetitionInterval (New-TimeSpan -Minutes ([int]$env:H_INTERVAL)); "
            "$startWhenAvailable=($env:H_MISSED -eq 'run_once'); "
            "$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable:$startWhenAvailable"
            " -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes ([int]$env:H_MAX_RUNTIME)); "
            "Register-ScheduledTask -TaskName $env:H_TASK_NAME -Action $action -Trigger $trigger"
            " -Settings $settings -Description $env:H_DESCRIPTION | Out-Null; "
            "if($env:H_ENABLED -ne '1'){Disable-ScheduledTask -TaskName $env:H_TASK_NAME | Out-Null}; "
            "$task=Get-ScheduledTask -TaskName $env:H_TASK_NAME; "
            "[pscustomobject]@{taskName=$task.TaskName;created=$true;idempotent=$false;state=[string]$task.State}|ConvertTo-Json -Compress"
        )
        env = {
            "H_TASK_NAME": task_name,
            "H_DESCRIPTION": description,
            "H_EXEC": executable,
            "H_ARGS": argument_line,
            "H_CWD": working_directory or "",
            "H_INTERVAL": str(interval_minutes),
            "H_MISSED": missed_run_policy,
            "H_MAX_RUNTIME": str(max_runtime_minutes),
            "H_ENABLED": "1" if enabled else "0",
        }
        result = self.control_plane.invoke(
            "execution.run",
            {"shell": "powershell", "command": command, "timeout_seconds": 60, "env": env},
        )
        return {"task_name": task_name, "spec_hash": spec_hash, "result": result}

    def list_jobs(self, *, owner: str | None = None) -> Mapping[str, Any]:
        env: dict[str, str] = {}
        if owner is not None:
            owner = self._name(owner, "owner")
            env["H_OWNER_MARKER"] = self.owner_marker(owner)
        else:
            env["H_OWNER_MARKER"] = self.DESCRIPTION_PREFIX + " "
        command = (
            "$ErrorActionPreference='Stop'; "
            "$marker=$env:H_OWNER_MARKER; "
            "$items=Get-ScheduledTask | Where-Object { "
            "$_.TaskName -like 'HeavenMaintenance--*' -and $_.Description -like ($marker+'*') } | "
            "ForEach-Object { $info=Get-ScheduledTaskInfo -TaskName $_.TaskName; "
            "[pscustomobject]@{taskName=$_.TaskName;state=[string]$_.State;description=$_.Description;"
            "lastRunTime=$info.LastRunTime;nextRunTime=$info.NextRunTime;lastTaskResult=$info.LastTaskResult} }; "
            "@($items) | ConvertTo-Json -Depth 4 -Compress"
        )
        return self.control_plane.invoke(
            "execution.run",
            {"shell": "powershell", "command": command, "timeout_seconds": 30, "env": env},
        )

    def _mutate(self, owner: str, name: str, action: str, *, confirm: bool) -> Mapping[str, Any]:
        self._confirm(confirm)
        env = self._owned_env(owner, name)
        action = str(action).lower()
        if action not in {"enable", "disable", "run", "delete"}:
            raise ValueError("unsupported scheduled-maintenance action")
        scripts = {
            "enable": "Enable-ScheduledTask -TaskName $env:H_TASK_NAME | Out-Null",
            "disable": "Disable-ScheduledTask -TaskName $env:H_TASK_NAME | Out-Null",
            "run": (
                "if([string]$task.State -eq 'Disabled'){throw 'disabled scheduled job cannot run'}; "
                "Start-ScheduledTask -TaskName $env:H_TASK_NAME"
            ),
            "delete": "Unregister-ScheduledTask -TaskName $env:H_TASK_NAME -Confirm:$false",
        }
        command = (
            "$ErrorActionPreference='Stop'; "
            "$task=Get-ScheduledTask -TaskName $env:H_TASK_NAME -ErrorAction Stop; "
            "if(-not $task.Description.StartsWith($env:H_OWNER_MARKER)){throw 'scheduled-maintenance ownership mismatch'}; "
            + scripts[action]
            + "; [pscustomobject]@{taskName=$env:H_TASK_NAME;action=$env:H_ACTION;ok=$true}|ConvertTo-Json -Compress"
        )
        env["H_ACTION"] = action
        return self.control_plane.invoke(
            "execution.run",
            {"shell": "powershell", "command": command, "timeout_seconds": 60, "env": env},
        )

    def enable_job(self, owner: str, name: str, *, confirm: bool = False) -> Mapping[str, Any]:
        return self._mutate(owner, name, "enable", confirm=confirm)

    def disable_job(self, owner: str, name: str, *, confirm: bool = False) -> Mapping[str, Any]:
        return self._mutate(owner, name, "disable", confirm=confirm)

    def run_now(self, owner: str, name: str, *, confirm: bool = False) -> Mapping[str, Any]:
        return self._mutate(owner, name, "run", confirm=confirm)

    def delete_job(self, owner: str, name: str, *, confirm: bool = False) -> Mapping[str, Any]:
        return self._mutate(owner, name, "delete", confirm=confirm)
