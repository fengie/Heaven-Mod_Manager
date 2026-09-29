from __future__ import annotations
import urllib.parse
from typing import Any,Mapping,Protocol

class BridgeTransport(Protocol):
    def request(self,action:str,params:Mapping[str,Any],timeout_seconds:int)->Mapping[str,Any]: ...

_BROWSERS={"edge":"msedge.exe","chrome":"chrome.exe","brave":"brave.exe","firefox":"firefox.exe"}

class BrowserPlugin:
    """Desktop-browser automation using app/window/UIA primitives without claiming DOM/CDP support."""
    def __init__(self,bridge:BridgeTransport): self.bridge=bridge

    def _req(self,a:str,p:Mapping[str,Any],t:int=30)->Mapping[str,Any]:
        r=self.bridge.request(a,p,t)
        if not isinstance(r,Mapping): raise RuntimeError("bridge returned non-object")
        if str(r.get("status") or "").lower()=="error" or r.get("error"):
            raise RuntimeError(str(r.get("error") or "browser action failed"))
        return r

    @staticmethod
    def _url(value:Any)->str:
        if not isinstance(value,str) or len(value)>8192: raise ValueError("invalid URL")
        u=urllib.parse.urlsplit(value)
        if u.scheme not in {"http","https"} or not u.netloc: raise ValueError("only http/https URLs are allowed")
        return value

    def open(self,url:str,*,browser:str="edge")->Mapping[str,Any]:
        url=self._url(url); key=str(browser).lower()
        if key not in _BROWSERS: raise ValueError("unsupported browser")
        return self._req("app_launch",{"path":_BROWSERS[key],"args":[url],"detached":True},30)

    def navigate(self,hwnd:int,url:str)->dict[str,Any]:
        if not isinstance(hwnd,int) or hwnd<=0: raise ValueError("hwnd must be positive")
        url=self._url(url)
        results=[
            self._req("window_focus",{"hwnd":hwnd},15),
            self._req("gui_key",{"key":"l","modifiers":["ctrl"]},15),
            self._req("gui_type",{"text":url},15),
            self._req("gui_key",{"key":"enter"},15),
        ]
        return {"results":results}

    def find_control(self,hwnd:int,selector:Mapping[str,Any],*,wait_ms:int=0)->Mapping[str,Any]:
        if not isinstance(hwnd,int) or hwnd<=0: raise ValueError("hwnd must be positive")
        if not isinstance(selector,Mapping) or not selector: raise ValueError("selector required")
        return self._req("uia_find",{**dict(selector),"hwnd":hwnd,"wait_ms":wait_ms},max(15,wait_ms//1000+10))

    def invoke_control(self,hwnd:int,selector:Mapping[str,Any])->Mapping[str,Any]:
        if not isinstance(selector,Mapping) or not selector: raise ValueError("selector required")
        return self._req("uia_invoke",{**dict(selector),"hwnd":hwnd},30)

    def type_into_control(self,hwnd:int,selector:Mapping[str,Any],text:str)->Mapping[str,Any]:
        if not isinstance(text,str) or len(text)>10000: raise ValueError("text invalid/too large")
        if not isinstance(selector,Mapping) or not selector: raise ValueError("selector required")
        return self._req("uia_set_value",{**dict(selector),"hwnd":hwnd,"value":text,"allow_relay_text":True},30)

    def screenshot(self,*,scope:str="primary")->Mapping[str,Any]:
        if scope not in {"primary","all"}: raise ValueError("scope must be primary or all")
        return self._req("screenshot",{"scope":scope},30)

    def wait_window(self,*,title:str|None=None,pid:int|None=None,timeout_seconds:int=30)->Mapping[str,Any]:
        if not isinstance(timeout_seconds,int) or not 1<=timeout_seconds<=300:
            raise ValueError("timeout_seconds must be 1..300")
        params={"condition":"window_exists","timeout_seconds":timeout_seconds}
        if title: params["title"]=title
        if pid: params["pid"]=pid
        if not title and not pid: raise ValueError("title or pid is required")
        return self._req("wait_for",params,timeout_seconds+10)
