from __future__ import annotations

import json
import re
from typing import Any, Mapping, Protocol, Sequence

_BRANCH_RE=re.compile(r"^(?![-./])(?!.*\.\.)(?!.*//)(?!.*@\{)(?!.*[/.]$)[A-Za-z0-9._/-]{1,200}$")


class ControlPlaneLike(Protocol):
    def invoke(self,capability_name:str,payload:Mapping[str,Any]|None=None,*,req_id:str|None=None,permissions:Sequence[str]|None=None)->dict[str,Any]: ...


class GitOpsPlugin:
    """Structured Git operations and an explicit integrate-to-main workflow."""

    def __init__(self,control_plane:ControlPlaneLike):
        self.control_plane=control_plane

    @staticmethod
    def _repo(value:Any)->str:
        if not isinstance(value,str) or not value.strip() or len(value)>2048:
            raise ValueError("repo must be a non-empty path")
        v=value.strip().replace("\\","/")
        if any(part==".." for part in v.split("/")):
            raise ValueError("repo path traversal is not allowed")
        return value.strip()

    @staticmethod
    def _branch(value:Any)->str:
        v=str(value or "").strip()
        if not _BRANCH_RE.fullmatch(v):
            raise ValueError("invalid branch name")
        return v

    def _run(self,repo:str,command:str,*,env:Mapping[str,str]|None=None,timeout:int=120)->dict[str,Any]:
        return self.control_plane.invoke("execution.run",{
            "shell":"powershell","command":command,"cwd":self._repo(repo),
            "timeout_seconds":timeout,"env":dict(env or {})
        })

    def log(self,repo:str,*,limit:int=20)->dict[str,Any]:
        if not isinstance(limit,int) or not 1<=limit<=200: raise ValueError("limit must be 1..200")
        return self._run(repo,
            "$ErrorActionPreference='Stop'; git log --date=iso-strict --pretty=format:'%H%x09%an%x09%ad%x09%s' -n ([int]$env:H_LIMIT)",
            env={"H_LIMIT":str(limit)},timeout=30)

    def show(self,repo:str,ref:str="HEAD")->dict[str,Any]:
        ref=self._branch(ref) if ref!="HEAD" else "HEAD"
        return self._run(repo,
            "$ErrorActionPreference='Stop'; git show --stat --oneline --decorate --no-renames $env:H_REF",
            env={"H_REF":ref},timeout=60)

    def branches(self,repo:str)->dict[str,Any]:
        return self._run(repo,
            "$ErrorActionPreference='Stop'; git branch --format='%(refname:short)%09%(objectname)%09%(upstream:short)'",
            timeout=30)

    def fetch(self,repo:str)->dict[str,Any]:
        return self._run(repo,"$ErrorActionPreference='Stop'; git fetch --prune origin",timeout=180)

    def pull_ff(self,repo:str,branch:str="main")->dict[str,Any]:
        branch=self._branch(branch)
        return self._run(repo,
            "$ErrorActionPreference='Stop'; git switch $env:H_BRANCH; git pull --ff-only origin $env:H_BRANCH",
            env={"H_BRANCH":branch},timeout=180)

    def add(self,repo:str,paths:Sequence[str])->dict[str,Any]:
        if isinstance(paths,str) or not paths or len(paths)>200: raise ValueError("paths must contain 1..200 entries")
        cleaned=[]
        for p in paths:
            if not isinstance(p,str) or not p or len(p)>2048: raise ValueError("invalid git path")
            n=p.replace("\\","/")
            if n.startswith("/") or any(x==".." for x in n.split("/")): raise ValueError("git paths must be repository-relative")
            cleaned.append(p)
        return self._run(repo,
            "$ErrorActionPreference='Stop'; $p=$env:H_PATHS|ConvertFrom-Json; git add -- @p",
            env={"H_PATHS":json.dumps(cleaned,separators=(",",":"))},timeout=60)

    def commit(self,repo:str,message:str)->dict[str,Any]:
        if not isinstance(message,str) or not message.strip() or len(message)>2000: raise ValueError("invalid commit message")
        return self._run(repo,
            "$ErrorActionPreference='Stop'; git commit -m $env:H_MESSAGE",
            env={"H_MESSAGE":message.strip()},timeout=120)

    def push(self,repo:str,branch:str,*,confirm:bool=False)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required to push")
        branch=self._branch(branch)
        return self._run(repo,
            "$ErrorActionPreference='Stop'; git push origin $env:H_BRANCH",
            env={"H_BRANCH":branch},timeout=180)

    def delete_branch(self,repo:str,branch:str,*,remote:bool=True,confirm:bool=False)->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required to delete branches")
        branch=self._branch(branch)
        if branch in {"main","master"}: raise ValueError("refusing to delete canonical branch")
        cmd=(
            "$ErrorActionPreference='Stop'; "
            "$b=$env:H_BRANCH; "
            "$main=(git rev-parse origin/main).Trim(); "
            "$tip=(git rev-parse $b).Trim(); "
            "git merge-base --is-ancestor $tip $main; if($LASTEXITCODE -ne 0){throw 'branch contains work not on origin/main'}; "
            "git branch -D $b; "
            "if($env:H_REMOTE -eq '1'){git push origin --delete $b}"
        )
        return self._run(repo,cmd,env={"H_BRANCH":branch,"H_REMOTE":"1" if remote else "0"},timeout=180)

    def integrate_task_to_main(
        self,repo:str,task_branch:str,*,verify_command:str|None=None,confirm:bool=False
    )->dict[str,Any]:
        if not confirm: raise ValueError("confirm=True is required to integrate to main")
        task_branch=self._branch(task_branch)
        if task_branch in {"main","master"}: raise ValueError("task branch must not be main")
        if verify_command is not None and (not isinstance(verify_command,str) or len(verify_command)>16000):
            raise ValueError("verify_command is invalid")
        cmd=(
            "$ErrorActionPreference='Stop'; "
            "git fetch --prune origin; "
            "if(git status --porcelain){throw 'working tree is not clean'}; "
            "$b=$env:H_BRANCH; git switch $b; git rebase origin/main; "
            "if($env:H_VERIFY){Invoke-Expression $env:H_VERIFY; if($LASTEXITCODE -ne 0){throw 'verification failed'}}; "
            "git switch main; git pull --ff-only origin main; git merge --ff-only $b; "
            "git push origin main; "
            "$local=(git rev-parse HEAD).Trim(); "
            "$remote=((git ls-remote origin refs/heads/main|Select-Object -First 1)-split '\\s+')[0]; "
            "if($local -ne $remote){throw 'remote main verification failed'}; "
            "git branch -D $b; git push origin --delete $b; "
            "[pscustomobject]@{integrated=$true;main=$local;deletedBranch=$b}|ConvertTo-Json -Compress"
        )
        return self._run(repo,cmd,env={"H_BRANCH":task_branch,"H_VERIFY":verify_command or ""},timeout=1800)
