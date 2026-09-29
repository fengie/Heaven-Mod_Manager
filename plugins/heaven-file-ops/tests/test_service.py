import sys,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_file_ops import FileOpsPlugin

class Bridge:
    def __init__(self): self.calls=[]
    def request(self,a,p,t): self.calls.append((a,dict(p),t)); return {"status":"completed","data":{}}

class FileOpsTests(unittest.TestCase):
    def test_delete_requires_confirmation(self):
        with self.assertRaises(ValueError): FileOpsPlugin(Bridge()).delete("C:/tmp/x")
    def test_traversal_rejected(self):
        with self.assertRaises(ValueError): FileOpsPlugin(Bridge()).info("../x")
    def test_copy_maps_to_bridge(self):
        b=Bridge(); FileOpsPlugin(b).copy("C:/a","C:/b",confirm=True)
        self.assertEqual(b.calls[0][0],"fs_copy")
    def test_binary_length_bounded(self):
        with self.assertRaises(ValueError): FileOpsPlugin(Bridge()).read_binary("C:/a",length=2_000_000)

if __name__=="__main__":unittest.main()
