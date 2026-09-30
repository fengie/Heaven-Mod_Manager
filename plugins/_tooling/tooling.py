from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any, Iterable

INDEX_SCHEMA = "heaven-toolbox/index/v1"
GENERIC = {"heaven-control-plane"}
MUTATING = {
    "act","add","cancel","claim","clear","commit","complete","copy","create","delete",
    "dispatch","execute","fail","fetch","input","install","invoke","move","navigate",
    "open","patch","publish","pull_ff","push","refresh","register","release","renew",
    "reserve","restart","run","start","stop","type_control","write",
}
GAP_FIELDS = (
    "Status","Priority","Triggering use case","Why reusable","Existing capability audit",
    "Proposed owner/plugin boundary","Capability/API contract","Security / permission boundary",
    "Dependencies / reuse","Acceptance tests","Owner / branch / PR","Completion evidence",
)
GAP_HEADER = re.compile(r"^## (PG-(\d{3,})) — (.+)$", re.MULTILINE)


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def manifest_paths(root: Path) -> list[Path]:
    return sorted((root / "plugins").glob("*/manifest.json"))


def permission_class(capability: str, manifest: dict[str, Any]) -> str:
    declared = manifest.get("capability_permissions")
    if isinstance(declared, dict) and isinstance(declared.get(capability), str):
        return declared[capability]
    return "mutation" if capability.rsplit(".", 1)[-1] in MUTATING else "read"


def build_index(root: Path | None = None) -> dict[str, Any]:
    root = (root or repo_root()).resolve()
    packages: list[dict[str, Any]] = []
    providers: dict[str, list[str]] = {}
    names: set[str] = set()
    for path in manifest_paths(root):
        manifest = json.loads(path.read_text(encoding="utf-8"))
        name, version, caps = manifest.get("name"), manifest.get("version"), manifest.get("capabilities")
        if not isinstance(name, str) or not name or name in names:
            raise ValueError(f"{path}: invalid or duplicate plugin name")
        if not isinstance(version, str) or not version:
            raise ValueError(f"{path}: missing version")
        if not isinstance(caps, list) or any(not isinstance(x, str) or not x for x in caps) or len(caps) != len(set(caps)):
            raise ValueError(f"{path}: capabilities must be unique non-empty strings")
        if not (path.parent / "README.md").is_file():
            raise ValueError(f"{name}: missing README.md")
        if not (path.parent / "verify.py").is_file():
            raise ValueError(f"{name}: missing verify.py")
        names.add(name)
        caps = sorted(caps)
        for cap in caps:
            providers.setdefault(cap, []).append(name)
        tags = {x for x in name.split("-") if x and x != "heaven"}
        tags.update(cap.split(".", 1)[0] for cap in caps)
        packages.append({
            "name": name,
            "version": version,
            "manifest": path.relative_to(root).as_posix(),
            "readme": (path.parent / "README.md").relative_to(root).as_posix(),
            "entrypoint": manifest.get("entrypoint"),
            "capabilities": [{"name": cap, "permission_class": permission_class(cap, manifest)} for cap in caps],
            "task_tags": sorted(tags),
            "machine_scope": ["heaven", "heaven2"] if name.startswith("heaven-") else ["repository"],
            "preferred_precedence": 50 if name in GENERIC else 100,
            "validation_command": f"python .\\plugins\\{name}\\verify.py",
            "implementation_status": "implemented",
        })
    duplicates = {cap: owners for cap, owners in providers.items() if len(owners) > 1}
    if duplicates:
        raise ValueError("duplicate capability providers require explicit routing precedence: " + json.dumps(duplicates, sort_keys=True))
    return {"schema": INDEX_SCHEMA, "packages": sorted(packages, key=lambda p: p["name"])}


def validate_catalog(root: Path | None = None) -> list[str]:
    root = (root or repo_root()).resolve()
    errors: list[str] = []
    try:
        index = build_index(root)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        return [str(exc)]
    indexed_dirs = {Path(p["manifest"]).parent.name for p in index["packages"]}
    for directory in sorted((root / "plugins").iterdir()):
        if not directory.is_dir() or directory.name.startswith("_") or directory.name == "remote-desktop-commander-local":
            continue
        runtime_markers = (directory / "pyproject.toml").is_file() or (directory / "verify.py").is_file()
        if runtime_markers and directory.name not in indexed_dirs:
            errors.append(f"{directory.name}: runtime plugin directory missing manifest.json")
    return errors


def resolve(required: Iterable[str], context: dict[str, Any] | None = None, *, root: Path | None = None) -> list[dict[str, Any]]:
    root = (root or repo_root()).resolve()
    required = sorted({x.strip() for x in required if isinstance(x, str) and x.strip()})
    if not required:
        raise ValueError("at least one capability or task tag is required")
    context = context or {}
    runtime = context.get("runtime_capabilities")
    runtime_set = None if runtime is None else {x for x in runtime if isinstance(x, str)}
    machine = context.get("machine")
    wanted_tags = {x for x in context.get("task_tags", []) if isinstance(x, str)}
    matches = []
    for package in build_index(root)["packages"]:
        if machine and machine not in package["machine_scope"]:
            continue
        caps = {x["name"] for x in package["capabilities"]}
        tags = set(package["task_tags"])
        score, reasons, ok = 0, [], True
        for req in required:
            if req in caps:
                score += 1000; reasons.append(f"exact capability: {req}")
            elif req.endswith(".*") and any(cap.startswith(req[:-1]) for cap in caps):
                score += 600; reasons.append(f"capability namespace: {req}")
            elif req in tags:
                score += 250; reasons.append(f"task tag: {req}")
            elif any(cap.startswith(req + ".") for cap in caps):
                score += 200; reasons.append(f"capability prefix: {req}")
            else:
                ok = False; break
        if not ok:
            continue
        overlap = sorted(wanted_tags & tags)
        if overlap:
            score += 25 * len(overlap); reasons.append("context tags: " + ", ".join(overlap))
        exact = [req for req in required if req in caps]
        if runtime_set is None:
            availability = {"status":"unknown","evidence":"runtime capability evidence was not supplied"}
        else:
            available = all(req in runtime_set for req in exact)
            availability = {"status":"available" if available else "unavailable","evidence":"caller supplied current runtime capabilities"}
        matches.append({
            "plugin": package["name"], "score": score,
            "preferred_precedence": package["preferred_precedence"], "reasons": reasons,
            "availability": availability,
            "activation": {"read":package["readme"],"validate":package["validation_command"],"entrypoint":package.get("entrypoint")},
        })
    matches.sort(key=lambda x: (-x["score"], -x["preferred_precedence"], x["plugin"]))
    return matches


def parse_gaps(text: str) -> list[dict[str, Any]]:
    headers = list(GAP_HEADER.finditer(text))
    out = []
    for i, match in enumerate(headers):
        end = headers[i + 1].start() if i + 1 < len(headers) else len(text)
        body = text[match.end():end]
        fields = {}
        for field in GAP_FIELDS:
            found = re.search(rf"^- \*\*{re.escape(field)}:\*\* ?(.*)$", body, re.MULTILINE)
            if found:
                fields[field] = found.group(1).strip()
        out.append({"id":match.group(1),"number":int(match.group(2)),"title":match.group(3).strip(),"body":body,"fields":fields})
    return out


def validate_backlog(text: str) -> list[str]:
    entries = parse_gaps(text)
    if not entries:
        return ["no plugin-gap entries found"]
    errors = []
    ids = [e["id"] for e in entries]
    if len(ids) != len(set(ids)):
        errors.append("duplicate plugin-gap IDs")
    for entry in entries:
        missing = [field for field in GAP_FIELDS if field not in entry["fields"]]
        if missing:
            errors.append(f"{entry['id']} missing fields: {', '.join(missing)}")
    if [e["number"] for e in entries] != sorted(e["number"] for e in entries):
        errors.append("plugin-gap IDs must increase monotonically")
    return errors


def tokens(value: str) -> set[str]:
    return {x for x in re.findall(r"[a-z0-9]+", value.lower()) if len(x) >= 3 and x not in {"the","and","for","with","plugin"}}


def duplicate_gaps(text: str, title: str, use_case: str, capability: str) -> list[dict[str, Any]]:
    needle = tokens(" ".join((title, use_case, capability)))
    out = []
    for entry in parse_gaps(text):
        hay = tokens(entry["title"] + " " + entry["body"])
        overlap = len(needle & hay)
        score = overlap / max(1, len(needle))
        if overlap >= 2 and score >= .20:
            out.append({"id":entry["id"],"title":entry["title"],"score":round(score,3)})
    return sorted(out, key=lambda x: (-x["score"], x["id"]))[:8]


def draft_gap(text: str, title: str, use_case: str, capability: str, priority: str, boundary: str) -> str:
    number = max((e["number"] for e in parse_gaps(text)), default=0) + 1
    gap_id = f"PG-{number:03d}"
    return f"""## {gap_id} — {title}

- **Status:** PLANNED
- **Priority:** {priority}
- **Triggering use case:** {use_case}
- **Why reusable:** The capability is expected to recur and should have one verified implementation path.
- **Existing capability audit:** Search the toolbox index, manifests, backlog, active branches, and open PRs before implementation; extend an existing owner where possible.
- **Proposed owner/plugin boundary:** {boundary}
- **Capability/API contract:** Provide {capability} through a bounded structured interface with explicit inputs, outputs, and failure evidence.
- **Security / permission boundary:** Reuse authorization and opaque secret-handle boundaries; do not persist credentials, widen machine authority, bypass provider controls, or hide mutation confirmations.
- **Dependencies / reuse:** Reuse existing Heaven control-plane, shared security, state, workflow, and transport primitives where applicable.
- **Acceptance tests:** Cover success, invalid input, unavailable dependency, permission denial, bounded output, idempotency where relevant, and regression behavior.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.
"""


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Heaven plugin toolbox and gap planner")
    sub = parser.add_subparsers(dest="cmd", required=True)
    b = sub.add_parser("index-build"); b.add_argument("--output", type=Path)
    sub.add_parser("index-validate")
    r = sub.add_parser("resolve"); r.add_argument("required", nargs="+"); r.add_argument("--machine"); r.add_argument("--runtime-capability", action="append", default=[]); r.add_argument("--task-tag", action="append", default=[])
    sub.add_parser("gap-validate")
    g = sub.add_parser("gap-plan"); g.add_argument("--title", required=True); g.add_argument("--use-case", required=True); g.add_argument("--required-capability", required=True); g.add_argument("--priority", default="Medium", choices=["Critical","High","Medium","Low"]); g.add_argument("--owner-boundary", default="Choose the narrowest existing plugin boundary after capability audit."); g.add_argument("--allow-duplicate", action="store_true")
    args = parser.parse_args(argv)
    root = repo_root()
    backlog_path = root / "plugins" / "PLUGIN_GAP_BACKLOG.md"
    if args.cmd == "index-build":
        payload = json.dumps(build_index(root), indent=2) + "\n"
        if args.output: args.output.write_text(payload, encoding="utf-8")
        else: print(payload, end="")
        return 0
    if args.cmd == "index-validate":
        errors = validate_catalog(root)
    elif args.cmd == "gap-validate":
        errors = validate_backlog(backlog_path.read_text(encoding="utf-8"))
    elif args.cmd == "resolve":
        print(json.dumps(resolve(args.required, {"machine":args.machine,"runtime_capabilities":args.runtime_capability or None,"task_tags":args.task_tag}, root=root), indent=2)); return 0
    else:
        text = backlog_path.read_text(encoding="utf-8")
        dupes = duplicate_gaps(text, args.title, args.use_case, args.required_capability)
        if dupes and not args.allow_duplicate:
            for item in dupes: print(f"possible duplicate {item['id']}: {item['title']} (score={item['score']})")
            print("choose whether to extend an existing entry; use --allow-duplicate only after that decision"); return 2
        print(draft_gap(text, args.title, args.use_case, args.required_capability, args.priority, args.owner_boundary), end=""); return 0
    if errors:
        for error in errors: print(error)
        return 1
    print("valid")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
