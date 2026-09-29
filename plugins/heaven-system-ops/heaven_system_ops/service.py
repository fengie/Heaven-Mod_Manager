from __future__ import annotations

import ipaddress
import re
from typing import Any, Mapping, Protocol, Sequence

_NAME_RE=re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.:@/+\-]{0,199}$")
_HOST_RE=re.compile(r"^[A-Za-z0-9.-]{1,253}$")
_TARGET_RE=re.compile(r"^(?:[A-Za-z0-9._-]+@)?[A-Za-z0-9.-]{1,253}$")
_VM_RE=re.compile(r"^[A-Za-z0-9_. -]{1,128}$")
_DISTRO_RE=re.compile(r"^[A-Za-z0-9_. -]{1,128}$")

class ControlPlaneLike(Protocol):
    def invoke(self,capability_name:str,payload:Mapping[str,Any]|None=None,*,req_id:str|None=None,permissions:Sequence[str]|None=None)->dict[str,Any]: ...

class SystemOpsPlugin:
    """Docker/WSL/VM/package/network/Tailscale/SSH helpers over bounded execution."""

    def __init__(self,control_plane:ControlPlaneLike): self.control_plane=control_plane

    def _run(self,command:str,*,env:Mapping[str,str]|None=None,timeout:int=120)->dict[str,Any]:
        return self.control_plane.invoke("execution.run",{
            "shell":"powershell","command":command,"timeout_seconds":timeout,"env":dict(env or {})
        })

    @staticmethod
    def _name(value:Any,field:str="name")->str:
        v=str(value or "").strip()
        if not _NAME_RE.fullmatch(v) or v.startswith("-"): raise ValueError(f"invalid {field}")
        return v

    @staticmethod
    def _host(value:Any)->str:
        v=str(value or "").strip()
        try: ipaddress.ip_address(v); return v
        except ValueError: pass
        if not _HOST_RE.fullmatch(v) or ".." in v: raise ValueError("invalid host")
        return v

    def availability(self)->dict[str,Any]:
        cmd=(
            "$names='docker','wsl','winget','choco','python','npm','tailscale','ssh','scp'; "
            "$out=@{}; foreach($n in $names){$c=Get-Command $n -ErrorAction SilentlyContinue;"
            "$out[$n]=[bool]$c}; $out['hyperv']=[bool](Get-Command Get-VM -ErrorAction SilentlyContinue); "
            "$out|ConvertTo-Json -Compress"
        )
        return self._run(cmd,timeout=30)

    def docker_ps(self,*,all:bool=False)->dict[str,Any]:
        return self._run(
            "$ErrorActionPreference='Stop'; if($env:H_ALL -eq '1'){docker ps -a --format '{{json .}}'}else{docker ps --format '{{json .}}'}",
            env={"H_ALL":"1" if all else "0"},timeout=60)

    def docker_images(self)->dict[str,Any]:
        return self._run("$ErrorActionPreference='Stop'; docker images --format '{{json .}}'",timeout=60)

    def docker_action(self,container:str,action:str,*,confirm:bool=False)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for Docker container mutations")
        container=self._name(container,"container")
        action=str(action or "").lower()
        if action not in {"start","stop","restart"}: raise ValueError("invalid Docker action")
        cmd="$ErrorActionPreference='Stop'; $args=@($env:H_CONTAINER); & docker $env:H_ACTION @args"
        return self._run(cmd,env={"H_CONTAINER":container,"H_ACTION":action},timeout=180)

    def docker_run(
        self,image:str,*,name:str|None=None,args:Sequence[str]=(),confirm:bool=False
    )->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for docker run")
        image=self._name(image,"image")
        if name is not None: name=self._name(name,"container name")
        if isinstance(args,str) or len(args)>64 or any(not isinstance(x,str) or len(x)>2048 for x in args):
            raise ValueError("invalid docker arguments")
        import json
        cmd=(
            "$ErrorActionPreference='Stop'; $a=@('run','-d'); "
            "if($env:H_NAME){$a+=@('--name',$env:H_NAME)}; "
            "$a+=$env:H_ARGS|ConvertFrom-Json; $a+=$env:H_IMAGE; & docker @a"
        )
        return self._run(cmd,env={"H_IMAGE":image,"H_NAME":name or "","H_ARGS":json.dumps(list(args))},timeout=600)

    def wsl_list(self)->dict[str,Any]:
        return self._run("$ErrorActionPreference='Stop'; wsl.exe --list --verbose",timeout=30)

    def wsl_run(self,distro:str,command:str,*,confirm:bool=False,timeout_seconds:int=600)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for WSL command execution")
        distro=str(distro or "").strip()
        if not _DISTRO_RE.fullmatch(distro): raise ValueError("invalid WSL distribution name")
        if not isinstance(command,str) or not command.strip() or len(command)>32768: raise ValueError("invalid WSL command")
        cmd="$ErrorActionPreference='Stop'; $a=@('-d',$env:H_DISTRO,'--','bash','-lc',$env:H_COMMAND); & wsl.exe @a"
        return self._run(cmd,env={"H_DISTRO":distro,"H_COMMAND":command},timeout=timeout_seconds)

    def vm_list(self)->dict[str,Any]:
        return self._run(
            "$ErrorActionPreference='Stop'; Get-VM | Select-Object Name,State,CPUUsage,MemoryAssigned,Uptime | ConvertTo-Json -Compress",
            timeout=60)

    def vm_action(self,name:str,action:str,*,confirm:bool=False)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for VM mutations")
        name=str(name or "").strip()
        if not _VM_RE.fullmatch(name): raise ValueError("invalid VM name")
        action=str(action or "").lower()
        cmds={
            "start":"Start-VM -Name $env:H_VM -ErrorAction Stop",
            "stop":"Stop-VM -Name $env:H_VM -ErrorAction Stop",
            "restart":"Restart-VM -Name $env:H_VM -Force -ErrorAction Stop"
        }
        if action not in cmds: raise ValueError("VM action must be start, stop, or restart")
        return self._run("$ErrorActionPreference='Stop'; "+cmds[action],env={"H_VM":name},timeout=300)

    def package_search(self,manager:str,query:str)->dict[str,Any]:
        manager=str(manager or "").lower()
        if manager not in {"winget","choco","pip","npm"}: raise ValueError("unsupported package manager")
        if not isinstance(query,str) or not query.strip() or len(query)>200: raise ValueError("invalid package query")
        commands={
            "winget":"winget search -- $env:H_QUERY",
            "choco":"choco search $env:H_QUERY --limit-output",
            "pip":"python -m pip index versions $env:H_QUERY",
            "npm":"npm search --parseable -- $env:H_QUERY"
        }
        return self._run("$ErrorActionPreference='Stop'; "+commands[manager],env={"H_QUERY":query.strip()},timeout=120)

    def package_install(self,manager:str,package:str,*,confirm:bool=False)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for package installation")
        manager=str(manager or "").lower()
        if manager not in {"winget","choco","pip","npm"}: raise ValueError("unsupported package manager")
        package=self._name(package,"package")
        commands={
            "winget":"winget install --id $env:H_PACKAGE --exact --accept-source-agreements --accept-package-agreements",
            "choco":"choco install $env:H_PACKAGE -y",
            "pip":"python -m pip install $env:H_PACKAGE",
            "npm":"npm install -g $env:H_PACKAGE"
        }
        return self._run("$ErrorActionPreference='Stop'; "+commands[manager],env={"H_PACKAGE":package},timeout=1800)

    def dns_lookup(self,host:str)->dict[str,Any]:
        host=self._host(host)
        return self._run(
            "$ErrorActionPreference='Stop'; Resolve-DnsName -Name $env:H_HOST | Select-Object Name,Type,IPAddress,NameHost | ConvertTo-Json -Compress",
            env={"H_HOST":host},timeout=30)

    def tailscale_status(self)->dict[str,Any]:
        return self._run("$ErrorActionPreference='Stop'; tailscale status --json",timeout=30)

    def tailscale_ping(self,host:str)->dict[str,Any]:
        host=self._host(host)
        return self._run("$ErrorActionPreference='Stop'; tailscale ping $env:H_HOST",env={"H_HOST":host},timeout=60)

    def ssh_exec(self,target:str,command:str,*,confirm:bool=False,timeout_seconds:int=600)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for remote command execution")
        target=str(target or "").strip()
        if not _TARGET_RE.fullmatch(target) or ".." in target: raise ValueError("invalid SSH target")
        if not isinstance(command,str) or not command.strip() or len(command)>32768: raise ValueError("invalid remote command")
        cmd="$ErrorActionPreference='Stop'; $a=@($env:H_TARGET,$env:H_REMOTE); & ssh @a"
        return self._run(cmd,env={"H_TARGET":target,"H_REMOTE":command},timeout=timeout_seconds)

    def scp_copy(self,source:str,destination:str,*,confirm:bool=False,recursive:bool=False)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for SCP transfer")
        for value in (source,destination):
            if not isinstance(value,str) or not value.strip() or len(value)>4096 or "\n" in value or "\r" in value:
                raise ValueError("invalid SCP path")
        cmd=(
            "$ErrorActionPreference='Stop'; $a=@(); if($env:H_RECURSIVE -eq '1'){$a+='-r'}; "
            "$a+=@($env:H_SOURCE,$env:H_DEST); & scp @a"
        )
        return self._run(cmd,env={"H_SOURCE":source,"H_DEST":destination,"H_RECURSIVE":"1" if recursive else "0"},timeout=1800)
