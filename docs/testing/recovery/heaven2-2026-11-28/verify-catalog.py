import argparse
import hashlib
import json
import sqlite3
from collections import Counter
from pathlib import Path

def seal(value):
    return hashlib.sha256(json.dumps(value, ensure_ascii=True, sort_keys=True).encode()).hexdigest()

def snapshot(path, root, wrappers, expected_names):
    with sqlite3.connect(path.as_uri()+"?mode=ro", uri=True) as db:
        db.execute("pragma query_only=on")
        db.execute("begin")
        mods = db.execute("select id,display_name,source_path,enabled,priority,family_id from mods order by id").fetchall()
        files = db.execute("select mod_id,path,blob_sha256,length from mod_files order by mod_id,path").fetchall()
        manifests = db.execute("select * from deployment_manifest order by path").fetchall()
        profiles = db.execute("select * from profile_mods order by profile_id,mod_id").fetchall()
        rules = db.execute("select * from conflict_rules order by id").fetchall()
        files_by_mod = {}
        for entry in files:
            files_by_mod.setdefault(entry[0], []).append(entry)
        wrapper_rows = []
        indexed = Counter()
        probes = {}
        duplicate_rows = {}
        for item in mods:
            mod_id, name, source, enabled, *_ = item
            source_path = Path(source)
            package_files = files_by_mod.get(mod_id, [])
            if source_path.parent == root and source_path.name in wrappers:
                wrapper_rows.append([name, enabled, len(package_files)])
            if package_files:
                indexed["enabled" if enabled else "disabled"] += 1
                pair_by_ext = {Path(p).suffix.casefold(): digest for _, p, digest, _ in package_files}
                if len(package_files) == 2 and pair_by_ext.keys() == {".pak", ".sig"}:
                    duplicate_rows.setdefault(tuple(sorted(pair_by_ext.items())), []).append((name, bool(enabled)))
            if name in expected_names:
                probes[name] = {"enabled": bool(enabled), "targets": [p for _,p,_,_ in package_files],
                    "matchesSource": len(package_files) == 2 and all(
                        (candidate := source_path/Path(p).name).is_file() and
                        hashlib.file_digest(candidate.open("rb"),"sha256").hexdigest() == digest
                        for _,p,digest,_ in package_files)}
        active_ids = {m[0] for m in mods if m[3]}
        return dict(rows=len(mods), files=len(files), deployed=len(manifests), active=len(active_ids),
            indexed=indexed, wrapperRows=wrapper_rows, sample=probes,
            duplicateIndexedPairs=[group for group in duplicate_rows.values() if len(group)>1],
            fullSeal=seal((mods,files,manifests,profiles,rules)), activeAndDeploymentSeal=seal(
                ([m for m in mods if m[0] in active_ids], [row for row in files if row[0] in active_ids],manifests,profiles,rules)))

def source_shape(root, wrappers):
    return seal([(str(p.relative_to(root)), s.st_size, s.st_mtime_ns) for w in sorted(wrappers)
        for p in sorted((root/w).rglob("*")) if p.is_file() and not p.is_symlink() for s in [p.stat()]])

p=argparse.ArgumentParser()
p.add_argument("catalog",type=Path)
p.add_argument("source",type=Path)
p.add_argument("expectations",type=Path,help="Local JSON with wrappers and expectedNames arrays")
a=p.parse_args()
expected = json.loads(a.expectations.read_text(encoding="utf-8"))
wrappers = set(expected["wrappers"])
expected_names = set(expected["expectedNames"])
source = a.source.resolve()
print(json.dumps({**snapshot(a.catalog.resolve(), source, wrappers, expected_names),
                  "sourceShapeSeal": source_shape(source, wrappers)}, ensure_ascii=False))
