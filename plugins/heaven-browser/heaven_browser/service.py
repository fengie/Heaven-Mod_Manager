from __future__ import annotations
import urllib.parse
from typing import Any,Mapping,Protocol

class BridgeTransport(Protocol):
    def request(self,action:str,params:Mapping[str,Any],timeout_seconds:int)->Mapping[str,Any]: ...

_BROWSERS={"edge":"msedge.exe","chrome":"chrome.exe","brave":"brave.exe","firefox":"firefox.exe"}

class BrowserPlugin:
    """Desktop-browser automation using app/window/UIA primitives without claiming DOM/CDP support."""
    def __init__(self,bridge:BridgeTransport,deep:Any|None=None): self.bridge=bridge; self.deep=deep

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
        if u.username or u.password: raise ValueError("URLs must not contain embedded credentials")
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

    def deep_status(self)->dict[str,Any]:
        return {"available":self.deep is not None,"provider":type(self.deep).__name__ if self.deep is not None else None}

    def _deep_provider(self)->Any:
        if self.deep is None:
            raise RuntimeError("deep browser provider is not configured")
        return self.deep

    def deep_create_session(self,url:str,*,headless:bool=True,timeout_ms:int=30000)->Mapping[str,Any]:
        return self._deep_provider().create_session(self._url(url),headless=headless,timeout_ms=timeout_ms)

    def deep_attach_cdp(self,endpoint:str)->Mapping[str,Any]:
        return self._deep_provider().attach_cdp(endpoint)

    def deep_navigate(self,session_id:str,url:str,*,tab_id:str|None=None,timeout_ms:int=30000)->Mapping[str,Any]:
        return self._deep_provider().navigate(session_id,self._url(url),tab_id=tab_id,timeout_ms=timeout_ms)

    def deep_query(self,session_id:str,selector:str,*,tab_id:str|None=None,limit:int=20)->Mapping[str,Any]:
        return self._deep_provider().query(session_id,selector,tab_id=tab_id,limit=limit)

    def deep_click(self,session_id:str,selector:str,*,tab_id:str|None=None,timeout_ms:int=30000)->Mapping[str,Any]:
        return self._deep_provider().click(session_id,selector,tab_id=tab_id,timeout_ms=timeout_ms)

    def deep_fill(self,session_id:str,selector:str,text:str,*,tab_id:str|None=None,secret:bool=False,timeout_ms:int=30000)->Mapping[str,Any]:
        if secret:
            raise ValueError("secret form fill is not exposed through relay-safe browser APIs")
        return self._deep_provider().fill(session_id,selector,text,tab_id=tab_id,secret=False,timeout_ms=timeout_ms)

    def deep_wait(self,session_id:str,selector:str,*,tab_id:str|None=None,state:str="visible",timeout_ms:int=30000)->Mapping[str,Any]:
        return self._deep_provider().wait(session_id,selector,tab_id=tab_id,state=state,timeout_ms=timeout_ms)

    def deep_tabs(self,session_id:str)->list[dict[str,Any]]:
        return self._deep_provider().tabs(session_id)

    def deep_open_tab(self,session_id:str,url:str,*,timeout_ms:int=30000)->Mapping[str,Any]:
        return self._deep_provider().open_tab(session_id,self._url(url),timeout_ms=timeout_ms)

    def deep_close_tab(self,session_id:str,tab_id:str)->Mapping[str,Any]:
        return self._deep_provider().close_tab(session_id,tab_id)

    def deep_download(self,session_id:str,selector:str,*,tab_id:str|None=None,filename:str|None=None,timeout_ms:int=30000)->Mapping[str,Any]:
        return self._deep_provider().download(session_id,selector,tab_id=tab_id,filename=filename,timeout_ms=timeout_ms)

    def deep_screenshot(self,session_id:str,filename:str,*,tab_id:str|None=None,full_page:bool=False)->Mapping[str,Any]:
        return self._deep_provider().screenshot(session_id,filename,tab_id=tab_id,full_page=full_page)

    def deep_console_summary(self,session_id:str,*,limit:int=50)->Mapping[str,Any]:
        return self._deep_provider().console_summary(session_id,limit=limit)

    def deep_network_summary(self,session_id:str,*,limit:int=50)->Mapping[str,Any]:
        return self._deep_provider().network_summary(session_id,limit=limit)

    def deep_close_session(self,session_id:str)->Mapping[str,Any]:
        return self._deep_provider().close_session(session_id)

