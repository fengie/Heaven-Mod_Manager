from __future__ import annotations
from typing import Any,Mapping,Protocol

class BridgeTransport(Protocol):
    def request(self,action:str,params:Mapping[str,Any],timeout_seconds:int)->Mapping[str,Any]: ...

class FileOpsPlugin:
    """Structured filesystem mutation/binary helpers over proven Heaven Bridge actions."""
    def __init__(self,bridge:BridgeTransport): self.bridge=bridge

    @staticmethod
    def _path(value:Any,field:str="path")->str:
        if not isinstance(value,str) or not value.strip() or len(value)>4096:
            raise ValueError(f"{field} must be a non-empty path")
        v=value.strip().replace("\\","/")
        if any(part==".." for part in v.split("/")) or "\x00" in value:
            raise ValueError(f"{field} contains forbidden traversal/NUL")
        return value.strip()

    def _req(self,action:str,params:Mapping[str,Any],timeout:int=60)->Mapping[str,Any]:
        result=self.bridge.request(action,params,timeout)
        if not isinstance(result,Mapping): raise RuntimeError("bridge returned a non-object result")
        if str(result.get("status") or "").lower()=="error" or result.get("error"):
            raise RuntimeError(str(result.get("error") or result.get("stderr") or "bridge action failed"))
        return result

    def info(self,path:str)->Mapping[str,Any]:
        return self._req("fs_info",{"path":self._path(path)},30)

    def list(self,path:str,*,depth:int=1,max_items:int=1000,offset:int=0)->Mapping[str,Any]:
        if not isinstance(depth,int) or not 0<=depth<=20: raise ValueError("depth must be 0..20")
        if not isinstance(max_items,int) or not 1<=max_items<=5000: raise ValueError("max_items must be 1..5000")
        if not isinstance(offset,int) or offset<0: raise ValueError("offset must be >=0")
        return self._req("fs_list",{"path":self._path(path),"depth":depth,"max_items":max_items,"offset":offset},60)

    def copy(self,source:str,destination:str,*,recursive:bool=False,confirm:bool=False)->Mapping[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for filesystem copy")
        return self._req("fs_copy",{
            "source":self._path(source,"source"),
            "destination":self._path(destination,"destination"),
            "recursive":bool(recursive)},300)

    def move(self,source:str,destination:str,*,confirm:bool=False)->Mapping[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for filesystem move")
        return self._req("fs_move",{
            "source":self._path(source,"source"),
            "destination":self._path(destination,"destination")},300)

    def delete(self,path:str,*,recursive:bool=False,confirm:bool=False)->Mapping[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for filesystem delete")
        return self._req("fs_delete",{"path":self._path(path),"recursive":bool(recursive)},300)

    def read_binary(self,path:str,*,offset:int=0,length:int=1_000_000)->Mapping[str,Any]:
        if not isinstance(offset,int) or offset<0: raise ValueError("offset must be >=0")
        if not isinstance(length,int) or not 1<=length<=1_000_000: raise ValueError("length must be 1..1000000")
        return self._req("fs_read_binary",{"path":self._path(path),"offset":offset,"length":length},60)

    def write_binary(self,path:str,data_base64:str,*,mode:str="rewrite",confirm:bool=False)->Mapping[str,Any]:
        if not confirm: raise ValueError("confirm=True is required for binary writes")
        if not isinstance(data_base64,str) or len(data_base64)>1_500_000:
            raise ValueError("data_base64 is invalid or too large")
        if mode not in {"rewrite","append"}: raise ValueError("mode must be rewrite or append")
        return self._req("fs_write_binary",{
            "path":self._path(path),"data_base64":data_base64,"mode":mode},120)
