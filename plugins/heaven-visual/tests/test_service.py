import struct,sys,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from heaven_visual import VisualPlugin

def tiny_png_header(width,height):
    return b"\x89PNG\r\n\x1a\n"+b"\x00\x00\x00\x0dIHDR"+struct.pack(">II",width,height)+b"\x08\x06\x00\x00\x00"

class VisualTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        (self.root/"x.png").write_bytes(tiny_png_header(1920,1200))
        self.p=VisualPlugin(self.root)
    def tearDown(self): self.tmp.cleanup()

    def test_probe_png_without_pillow(self):
        r=self.p.probe("x.png")
        self.assertEqual((r["width"],r["height"]),(1920,1200))
    def test_tile_plan_bounded_geometry(self):
        r=self.p.tile_plan("x.png",tile_width=1000,tile_height=700,overlap=100)
        self.assertGreater(r["count"],1)
        self.assertLessEqual(r["tiles"][-1]["right"],1920)
    def test_escape_rejected(self):
        with self.assertRaises(ValueError): self.p.probe("../x.png")
    def test_existing_transform_requires_confirmation(self):
        with self.assertRaises(ValueError): self.p.resize("x.png","x.png",10,10)

if __name__=="__main__":unittest.main()
