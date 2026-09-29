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
    def test_open_uses_argument_not_shell(self):
        b=B(); BrowserPlugin(b).open("https://example.com",browser="edge")
        self.assertEqual(b.calls[0][0],"app_launch")
        self.assertEqual(b.calls[0][1]["args"],["https://example.com"])
    def test_navigate_uses_focus_shortcut_type_enter(self):
        b=B(); BrowserPlugin(b).navigate(5,"https://example.com")
        self.assertEqual([x[0] for x in b.calls],["window_focus","gui_key","gui_type","gui_key"])
    def test_wait_requires_selector(self):
        with self.assertRaises(ValueError): BrowserPlugin(B()).wait_window()

if __name__=="__main__":unittest.main()
