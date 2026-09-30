import sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_browser import BrowserPlugin

class B:
    def __init__(self): self.calls=[]
    def request(self,a,p,t): self.calls.append((a,dict(p),t)); return {"status":"completed"}

class BrowserTests(unittest.TestCase):
    def test_blocks_non_http_scheme(self):
        with self.assertRaises(ValueError): BrowserPlugin(B()).open("javascript:alert(1)")
    def test_blocks_embedded_credentials(self):
        b=B()
        with self.assertRaises(ValueError): BrowserPlugin(b).open("https://user:pass@example.com")
        self.assertEqual([],b.calls)
    def test_open_uses_argument_not_shell(self):
        b=B(); BrowserPlugin(b).open("https://example.com",browser="edge")
        self.assertEqual(b.calls[0][0],"app_launch")
        self.assertEqual(b.calls[0][1]["args"],["https://example.com"])
    def test_navigate_uses_focus_shortcut_type_enter(self):
        b=B(); BrowserPlugin(b).navigate(5,"https://example.com")
        self.assertEqual([x[0] for x in b.calls],["window_focus","gui_key","gui_type","gui_key"])
    def test_wait_requires_selector(self):
        with self.assertRaises(ValueError): BrowserPlugin(B()).wait_window()


class D:
    def __init__(self): self.calls=[]
    def create_session(self,url,**kwargs): self.calls.append(("create",url,kwargs)); return {"session_id":"s"}
    def fill(self,*args,**kwargs): self.calls.append(("fill",args,kwargs)); return {"filled":True}

class DeepRoutingTests(unittest.TestCase):
    def test_deep_status_and_missing_provider(self):
        plugin=BrowserPlugin(B())
        self.assertFalse(plugin.deep_status()["available"])
        with self.assertRaises(RuntimeError):
            plugin.deep_create_session("https://example.com")

    def test_deep_create_routes_to_provider(self):
        d=D(); plugin=BrowserPlugin(B(),deep=d)
        result=plugin.deep_create_session("https://example.com",headless=False,timeout_ms=1234)
        self.assertEqual("s",result["session_id"])
        self.assertEqual("create",d.calls[0][0])
        self.assertFalse(d.calls[0][2]["headless"])

    def test_deep_fill_rejects_secret_before_provider(self):
        d=D(); plugin=BrowserPlugin(B(),deep=d)
        with self.assertRaises(ValueError):
            plugin.deep_fill("s","#password","secret",secret=True)
        self.assertEqual([],d.calls)

if __name__=="__main__":unittest.main()
