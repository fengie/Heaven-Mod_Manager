import sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_desktop import DesktopPlugin

class B:
    def __init__(self): self.calls=[]
    def request(self,a,p,t): self.calls.append((a,dict(p),t)); return {"status":"completed","data":{}}

class DesktopTests(unittest.TestCase):
    def test_observe_composes_three_reads(self):
        b=B(); DesktopPlugin(b).observe()
        self.assertEqual([x[0] for x in b.calls],["display_list","window_list","gui_cursor_get"])
    def test_close_requires_confirmation(self):
        with self.assertRaises(ValueError):
            DesktopPlugin(B()).act([{"action":"window.close","params":{"hwnd":1}}])
    def test_uia_value_requires_relay_opt_in(self):
        with self.assertRaises(ValueError):
            DesktopPlugin(B()).act([{"action":"uia.set_value","params":{"automation_id":"x","value":"abc"}}])
    def test_secret_action_not_exposed(self):
        with self.assertRaises(ValueError):
            DesktopPlugin(B()).act([{"action":"text.secret","params":{}}])

if __name__=="__main__":unittest.main()
