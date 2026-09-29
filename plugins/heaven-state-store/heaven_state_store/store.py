from __future__ import annotations

import hashlib
import json
import sqlite3
import time
import uuid
from pathlib import Path
from typing import Any, Mapping

_BLOCKED_METADATA_KEYS={"credentials","auth_data","session_material"}

class StateStore:
    """Durable local artifacts, optimistic checkpoints, and machine-health history."""

    def __init__(self,root:str|Path):
        self.root=Path(root).resolve()
        self.root.mkdir(parents=True,exist_ok=True)
        self.artifact_dir=self.root/"artifacts"
        self.artifact_dir.mkdir(exist_ok=True)
        self.db=self.root/"state.db"
        self._init()

    def _connect(self)->sqlite3.Connection:
        c=sqlite3.connect(self.db,timeout=30)
        c.row_factory=sqlite3.Row
        c.execute("PRAGMA foreign_keys=ON")
        c.execute("PRAGMA journal_mode=WAL")
        return c

    def _init(self)->None:
        with self._connect() as c:
            c.executescript("""
            CREATE TABLE IF NOT EXISTS artifacts(
                id TEXT PRIMARY KEY, sha256 TEXT NOT NULL, size INTEGER NOT NULL,
                mime TEXT NOT NULL, filename TEXT, metadata_json TEXT NOT NULL,
                path TEXT NOT NULL, created_at REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_artifacts_sha ON artifacts(sha256);
            CREATE TABLE IF NOT EXISTS checkpoints(
                key TEXT PRIMARY KEY, payload_json TEXT NOT NULL,
                revision INTEGER NOT NULL, updated_at REAL NOT NULL
            );
            CREATE TABLE IF NOT EXISTS health(
                id INTEGER PRIMARY KEY AUTOINCREMENT, machine TEXT NOT NULL,
                payload_json TEXT NOT NULL, created_at REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_health_machine_time ON health(machine,created_at DESC);
            """)

    @staticmethod
    def _safe_json(value:Any,max_bytes:int=512_000)->str:
        def walk(v:Any)->None:
            if isinstance(v,Mapping):
                for k,x in v.items():
                    if str(k).lower() in _BLOCKED_METADATA_KEYS:
                        raise ValueError("credential-bearing metadata keys are not allowed in persisted state")
                    walk(x)
            elif isinstance(v,list):
                for x in v: walk(x)
        walk(value)
        raw=json.dumps(value,separators=(",",":"),ensure_ascii=False)
        if len(raw.encode("utf-8"))>max_bytes: raise ValueError("persisted JSON exceeds size limit")
        return raw

    def publish_artifact(
        self,data:bytes|str,*,filename:str|None=None,mime:str="application/octet-stream",
        metadata:Mapping[str,Any]|None=None
    )->dict[str,Any]:
        raw=data.encode("utf-8") if isinstance(data,str) else bytes(data)
        if len(raw)>100_000_000: raise ValueError("artifact exceeds 100MB limit")
        metadata_json=self._safe_json(dict(metadata or {}))
        digest=hashlib.sha256(raw).hexdigest()
        artifact_id=uuid.uuid4().hex
        path=self.artifact_dir/digest
        if not path.exists():
            tmp=path.with_suffix(".tmp-"+artifact_id)
            tmp.write_bytes(raw); tmp.replace(path)
        with self._connect() as c:
            c.execute(
                "INSERT INTO artifacts VALUES(?,?,?,?,?,?,?,?)",
                (artifact_id,digest,len(raw),str(mime)[:256],filename,metadata_json,str(path),time.time())
            )
        return {"id":artifact_id,"sha256":digest,"size":len(raw),"path":str(path)}

    def artifact_info(self,artifact_id:str)->dict[str,Any]:
        with self._connect() as c:
            row=c.execute("SELECT * FROM artifacts WHERE id=?",(artifact_id,)).fetchone()
        if row is None: raise KeyError(artifact_id)
        return {
            "id":row["id"],"sha256":row["sha256"],"size":row["size"],"mime":row["mime"],
            "filename":row["filename"],"metadata":json.loads(row["metadata_json"]),
            "created_at":row["created_at"]
        }

    def read_artifact(self,artifact_id:str,*,offset:int=0,length:int=1_000_000)->bytes:
        if not isinstance(offset,int) or offset<0: raise ValueError("offset must be >=0")
        if not isinstance(length,int) or not 1<=length<=1_000_000: raise ValueError("length must be 1..1000000")
        with self._connect() as c:
            row=c.execute("SELECT path FROM artifacts WHERE id=?",(artifact_id,)).fetchone()
        if row is None: raise KeyError(artifact_id)
        with open(row["path"],"rb") as f:
            f.seek(offset); return f.read(length)

    def compare_artifacts(self,left_id:str,right_id:str)->dict[str,Any]:
        left=self.artifact_info(left_id); right=self.artifact_info(right_id)
        return {"equal":left["sha256"]==right["sha256"],"left":left,"right":right}

    def put_checkpoint(self,key:str,payload:Any,*,expected_revision:int|None=None)->dict[str,Any]:
        if not isinstance(key,str) or not key or len(key)>256: raise ValueError("invalid checkpoint key")
        raw=self._safe_json(payload)
        now=time.time()
        with self._connect() as c:
            c.execute("BEGIN IMMEDIATE")
            row=c.execute("SELECT revision FROM checkpoints WHERE key=?",(key,)).fetchone()
            current=None if row is None else int(row["revision"])
            if expected_revision is not None and current!=expected_revision:
                c.rollback(); raise ValueError(f"checkpoint revision conflict: expected {expected_revision}, current {current}")
            revision=1 if current is None else current+1
            c.execute(
                """INSERT INTO checkpoints(key,payload_json,revision,updated_at) VALUES(?,?,?,?)
                   ON CONFLICT(key) DO UPDATE SET payload_json=excluded.payload_json,
                   revision=excluded.revision,updated_at=excluded.updated_at""",
                (key,raw,revision,now)
            )
            c.commit()
        return {"key":key,"revision":revision,"updated_at":now}

    def get_checkpoint(self,key:str)->dict[str,Any]|None:
        with self._connect() as c:
            row=c.execute("SELECT * FROM checkpoints WHERE key=?",(key,)).fetchone()
        if row is None: return None
        return {"key":row["key"],"payload":json.loads(row["payload_json"]),"revision":row["revision"],"updated_at":row["updated_at"]}

    def record_health(self,machine:str,metrics:Mapping[str,Any])->None:
        if not isinstance(machine,str) or not machine or len(machine)>128: raise ValueError("invalid machine")
        raw=self._safe_json(dict(metrics),max_bytes=128_000)
        with self._connect() as c:
            c.execute("INSERT INTO health(machine,payload_json,created_at) VALUES(?,?,?)",(machine,raw,time.time()))

    def recent_health(self,machine:str,*,limit:int=50)->list[dict[str,Any]]:
        if not isinstance(limit,int) or not 1<=limit<=1000: raise ValueError("limit must be 1..1000")
        with self._connect() as c:
            rows=c.execute(
                "SELECT payload_json,created_at FROM health WHERE machine=? ORDER BY created_at DESC LIMIT ?",
                (machine,limit)
            ).fetchall()
        return [{"metrics":json.loads(r["payload_json"]),"created_at":r["created_at"]} for r in rows]
