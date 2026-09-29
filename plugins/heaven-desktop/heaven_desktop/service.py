from __future__ import annotations
from typing import Any,Mapping,Protocol,Sequence

class BridgeTransport(Protocol):
    def request(self,action:str,params:Mapping[str,Any],timeout_seconds:int)->Mapping[str,Any]: ...

_ACTIONS={
    "window.focus":"window_focus","window.move":"window_move","window.state":"window_state",
    "window.close":"window_close","mouse.move":"gui_mouse_move","mouse.click":"gui_mouse_click",
    "mouse.button":"gui_mouse_button","mouse.scroll":"gui_mouse_scroll","key.press":"gui_key",
    "text.type":"gui_type","uia.focus":"uia_focus","uia.invoke":"uia_invoke",
    "uia.set_value":"uia_set_value","uia.toggle":"uia_toggle","uia.select":"uia_select",
    "uia.expand":"uia_expand","uia.collapse":"uia_collapse"
}
_MUTATING_CONFIRM={"window.close"}

class DesktopPlugin:
    """Stable desktop observe/act wrapper over Heaven Bridge Win32/UIA primitives."""
    def __init__(self,bridge:BridgeTransport): self.bridge=bridge

    def _request(self,action:str,params:Mapping[str,Any],timeout:int=30)->Mapping[str,Any]:
        result=self.bridge.request(action,params,timeout)
        if not isinstance(result,Mapping): raise RuntimeError("bridge returned non-object")
        if str(result.get("status") or "").lower()=="error" or result.get("error"):
            raise RuntimeError(str(result.get("error") or "desktop bridge action failed"))
        return result

    def observe(self,*,include_screenshot:bool=False,scope:str="primary")->dict[str,Any]:
        if scope not in {"primary","all"}: raise ValueError("scope must be primary or all")
        out={
            "displays":self._request("display_list",{},30),
            "windows":self._request("window_list",{"limit":300,"visible_only":True},30),
            "cursor":self._request("gui_cursor_get",{},15),
        }
        if include_screenshot:
            out["screenshot"]=self._request("screenshot",{"scope":scope},30)
        return out

    def find(self,selector:Mapping[str,Any],*,wait_ms:int=0,max_nodes:int=250)->Mapping[str,Any]:
        if not isinstance(selector,Mapping) or not selector: raise ValueError("selector must be a non-empty object")
        if not isinstance(wait_ms,int) or not 0<=wait_ms<=30000: raise ValueError("wait_ms must be 0..30000")
        if not isinstance(max_nodes,int) or not 1<=max_nodes<=1000: raise ValueError("max_nodes must be 1..1000")
        return self._request("uia_find",{**dict(selector),"wait_ms":wait_ms,"max_nodes":max_nodes},max(15,wait_ms//1000+10))

    def act(self,actions:Sequence[Mapping[str,Any]])->dict[str,Any]:
        if isinstance(actions,(str,bytes)) or not isinstance(actions,Sequence) or not 1<=len(actions)<=50:
            raise ValueError("actions must contain 1..50 action objects")
        results=[]
        for item in actions:
            if not isinstance(item,Mapping): raise ValueError("each action must be an object")
            name=str(item.get("action") or "")
            if name not in _ACTIONS: raise ValueError(f"unsupported desktop action: {name}")
            if name in _MUTATING_CONFIRM and item.get("confirm") is not True:
                raise ValueError(f"{name} requires confirm=true")
            params=dict(item.get("params") or {})
            if name=="text.type":
                text=params.get("text")
                if not isinstance(text,str) or len(text)>10000: raise ValueError("typed text is invalid/too large")
            if name=="uia.set_value" and params.get("allow_relay_text") is not True:
                raise ValueError("uia.set_value requires allow_relay_text=true")
            results.append(self._request(_ACTIONS[name],params,30))
        return {"count":len(results),"results":results}

    def screenshot(self,*,scope:str="primary",monitor_index:int|None=None)->Mapping[str,Any]:
        if scope not in {"primary","all","monitor"}: raise ValueError("invalid screenshot scope")
        params={"scope":scope}
        if scope=="monitor":
            if not isinstance(monitor_index,int) or monitor_index<0: raise ValueError("monitor_index required")
            params["monitor_index"]=monitor_index
        return self._request("screenshot",params,30)
