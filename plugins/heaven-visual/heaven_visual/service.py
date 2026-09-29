from __future__ import annotations

import hashlib
import struct
from pathlib import Path
from typing import Any

class VisualPlugin:
    """Cheap local image probing and optional Pillow-backed transforms."""

    def __init__(self,allowed_root:str|Path):
        self.root=Path(allowed_root).resolve()
        self.root.mkdir(parents=True,exist_ok=True)

    def _path(self,value:str|Path,*,must_exist:bool=True)->Path:
        p=Path(value)
        if not p.is_absolute(): p=self.root/p
        p=p.resolve(strict=must_exist)
        try: p.relative_to(self.root)
        except ValueError as exc: raise ValueError("image path escapes allowed root") from exc
        return p

    @staticmethod
    def _jpeg_size(data:bytes)->tuple[int,int]:
        if not data.startswith(b"\xff\xd8"): raise ValueError("not a JPEG")
        i=2
        while i+9<len(data):
            if data[i]!=0xFF:
                i+=1; continue
            marker=data[i+1]; i+=2
            if marker in {0xD8,0xD9}: continue
            if i+2>len(data): break
            length=int.from_bytes(data[i:i+2],"big")
            if length<2 or i+length>len(data): break
            if marker in {0xC0,0xC1,0xC2,0xC3,0xC5,0xC6,0xC7,0xC9,0xCA,0xCB,0xCD,0xCE,0xCF}:
                h=int.from_bytes(data[i+3:i+5],"big"); w=int.from_bytes(data[i+5:i+7],"big")
                return w,h
            i+=length
        raise ValueError("JPEG dimensions not found")

    def probe(self,path:str|Path)->dict[str,Any]:
        p=self._path(path); data=p.read_bytes()
        fmt="unknown"; width=height=None
        if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data)>=24:
            width,height=struct.unpack(">II",data[16:24]); fmt="png"
        elif data.startswith((b"GIF87a",b"GIF89a")) and len(data)>=10:
            width,height=struct.unpack("<HH",data[6:10]); fmt="gif"
        elif data.startswith(b"\xff\xd8"):
            width,height=self._jpeg_size(data); fmt="jpeg"
        digest=hashlib.sha256(data).hexdigest()
        return {"path":str(p),"format":fmt,"width":width,"height":height,"bytes":len(data),"sha256":digest}

    def tile_plan(self,path:str|Path,*,tile_width:int=1024,tile_height:int=1024,overlap:int=0)->dict[str,Any]:
        info=self.probe(path)
        w,h=info["width"],info["height"]
        if w is None or h is None: raise ValueError("image dimensions are unavailable")
        if not all(isinstance(x,int) and x>0 for x in (tile_width,tile_height)): raise ValueError("tile dimensions must be positive")
        if not isinstance(overlap,int) or overlap<0 or overlap>=min(tile_width,tile_height):
            raise ValueError("overlap must be >=0 and smaller than each tile dimension")
        sx=tile_width-overlap; sy=tile_height-overlap
        boxes=[]
        y=0
        while y<h:
            x=0
            while x<w:
                boxes.append({"left":x,"top":y,"right":min(w,x+tile_width),"bottom":min(h,y+tile_height)})
                if x+tile_width>=w: break
                x+=sx
            if y+tile_height>=h: break
            y+=sy
        return {"image":info,"tiles":boxes,"count":len(boxes)}

    @staticmethod
    def _pillow():
        try:
            from PIL import Image
        except ImportError as exc:
            raise RuntimeError("Pillow is required for image transforms; install the optional Pillow dependency") from exc
        return Image

    def resize(
        self,source:str|Path,destination:str|Path,width:int,height:int,*,confirm_overwrite:bool=False
    )->dict[str,Any]:
        if not all(isinstance(x,int) and 1<=x<=32768 for x in (width,height)):
            raise ValueError("width/height must be 1..32768")
        src=self._path(source); dst=self._path(destination,must_exist=False)
        if dst.exists() and not confirm_overwrite: raise ValueError("destination exists; confirm_overwrite=True required")
        dst.parent.mkdir(parents=True,exist_ok=True)
        Image=self._pillow()
        with Image.open(src) as im:
            out=im.resize((width,height))
            out.save(dst)
        return self.probe(dst)

    def crop(
        self,source:str|Path,destination:str|Path,box:tuple[int,int,int,int],*,confirm_overwrite:bool=False
    )->dict[str,Any]:
        if not isinstance(box,tuple) or len(box)!=4 or any(not isinstance(x,int) for x in box):
            raise ValueError("box must be a four-integer tuple")
        l,t,r,b=box
        if l<0 or t<0 or r<=l or b<=t: raise ValueError("invalid crop box")
        src=self._path(source); dst=self._path(destination,must_exist=False)
        if dst.exists() and not confirm_overwrite: raise ValueError("destination exists; confirm_overwrite=True required")
        dst.parent.mkdir(parents=True,exist_ok=True)
        Image=self._pillow()
        with Image.open(src) as im:
            if r>im.width or b>im.height: raise ValueError("crop box exceeds source image")
            im.crop(box).save(dst)
        return self.probe(dst)
