from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

SEMVER_RE = re.compile(
    r"^(?P<major>0|[1-9]\d*)\.(?P<minor>0|[1-9]\d*)\.(?P<patch>0|[1-9]\d*)"
    r"(?:-(?P<prerelease>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$"
)

MANIFEST_CANDIDATES = (
    Path("manifest.json"),
    Path("plugin.json"),
    Path(".codex-plugin") / "plugin.json",
)


@dataclass(frozen=True)
class PluginInstall:
    name: str
    version: str
    path: Path
    manifest_path: Path


@dataclass(frozen=True, order=False)
class SemVer:
    major: int
    minor: int
    patch: int
    prerelease: tuple[tuple[int, object], ...]

    @classmethod
    def parse(cls, value: str) -> "SemVer | None":
        match = SEMVER_RE.fullmatch(value.strip())
        if not match:
            return None
        pre = match.group("prerelease")
        parts: list[tuple[int, object]] = []
        if pre:
            for token in pre.split("."):
                if token.isdigit():
                    parts.append((0, int(token)))
                else:
                    parts.append((1, token))
        return cls(
            int(match.group("major")),
            int(match.group("minor")),
            int(match.group("patch")),
            tuple(parts),
        )

    def _core(self) -> tuple[int, int, int]:
        return self.major, self.minor, self.patch

    def __lt__(self, other: "SemVer") -> bool:
        if self._core() != other._core():
            return self._core() < other._core()
        if not self.prerelease and other.prerelease:
            return False
        if self.prerelease and not other.prerelease:
            return True
        for left, right in zip(self.prerelease, other.prerelease):
            if left == right:
                continue
            if left[0] != right[0]:
                return left[0] < right[0]
            return left[1] < right[1]
        return len(self.prerelease) < len(other.prerelease)


def _resolved(path: Path) -> Path:
    return path.expanduser().resolve(strict=False)


def _is_within(path: Path, root: Path) -> bool:
    try:
        _resolved(path).relative_to(_resolved(root))
        return True
    except ValueError:
        return False


def default_roots() -> list[Path]:
    home = Path.home()
    roots = [
        home / ".codex" / "plugins",
        home / ".chatgpt" / "plugins",
    ]
    local = os.environ.get("LOCALAPPDATA")
    roaming = os.environ.get("APPDATA")
    if local:
        roots.extend(
            [
                Path(local) / "OpenAI" / "plugins",
                Path(local) / "ChatGPT" / "plugins",
                Path(local) / "MHW-Agent-Control" / "plugins",
            ]
        )
    if roaming:
        roots.extend(
            [
                Path(roaming) / "OpenAI" / "plugins",
                Path(roaming) / "ChatGPT" / "plugins",
            ]
        )
    extra = os.environ.get("MHW_PLUGIN_INSTALL_ROOTS", "")
    for item in extra.split(os.pathsep):
        if item.strip():
            roots.append(Path(item.strip()))
    seen: set[Path] = set()
    unique: list[Path] = []
    for root in roots:
        resolved = _resolved(root)
        if resolved not in seen:
            seen.add(resolved)
            unique.append(resolved)
    return unique


def _read_manifest(directory: Path) -> PluginInstall | None:
    for relative in MANIFEST_CANDIDATES:
        manifest_path = directory / relative
        if not manifest_path.is_file():
            continue
        try:
            raw = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
        except (OSError, json.JSONDecodeError):
            continue
        name = raw.get("name")
        version = raw.get("version")
        if isinstance(name, str) and name.strip() and isinstance(version, str) and version.strip():
            return PluginInstall(name.strip(), version.strip(), directory, manifest_path)
    return None


def discover(root: Path, *, max_depth: int = 2) -> list[PluginInstall]:
    root = _resolved(root)
    if not root.is_dir():
        return []

    discovered: list[PluginInstall] = []
    pending: list[tuple[Path, int]] = [(root, 0)]
    seen: set[Path] = set()
    while pending:
        current, depth = pending.pop()
        if current in seen:
            continue
        seen.add(current)
        if current != root:
            plugin = _read_manifest(current)
            if plugin is not None:
                discovered.append(plugin)
                continue
        if depth >= max_depth:
            continue
        try:
            children = list(current.iterdir())
        except OSError:
            continue
        for child in children:
            try:
                if child.is_symlink():
                    continue
                if child.is_dir():
                    pending.append((_resolved(child), depth + 1))
            except OSError:
                continue
    return discovered


def _canonical_source_roots(repo_root: Path | None) -> list[Path]:
    if repo_root is None:
        return []
    repo_root = _resolved(repo_root)
    return [
        repo_root / "plugins",
        repo_root / "heaven-bridge" / "plugin",
        repo_root / "tools" / "agent-control" / "chatgpt-plugin",
    ]


def plan_prune(
    roots: Iterable[Path],
    *,
    repo_root: Path | None = None,
) -> dict[str, object]:
    canonical_roots = _canonical_source_roots(repo_root)
    installs: list[PluginInstall] = []
    scanned_roots: list[str] = []

    for root in roots:
        resolved_root = _resolved(root)
        scanned_roots.append(str(resolved_root))
        installs.extend(discover(resolved_root))

    groups: dict[str, list[PluginInstall]] = {}
    for install in installs:
        groups.setdefault(install.name.casefold(), []).append(install)

    remove: list[dict[str, str]] = []
    keep: list[dict[str, str]] = []
    skipped: list[dict[str, str]] = []

    for items in groups.values():
        parsed: list[tuple[SemVer, PluginInstall]] = []
        for item in items:
            semver = SemVer.parse(item.version)
            if semver is None:
                skipped.append(
                    {
                        "name": item.name,
                        "version": item.version,
                        "path": str(item.path),
                        "reason": "non-semver-version",
                    }
                )
                continue
            parsed.append((semver, item))
        if not parsed:
            continue

        highest = max(version for version, _ in parsed)
        highest_items = [item for version, item in parsed if not (version < highest) and not (highest < version)]

        for version, item in parsed:
            path = _resolved(item.path)
            if any(_is_within(path, canonical) for canonical in canonical_roots):
                keep.append(
                    {
                        "name": item.name,
                        "version": item.version,
                        "path": str(path),
                        "reason": "canonical-repo-source",
                    }
                )
                continue
            if version < highest:
                remove.append(
                    {
                        "name": item.name,
                        "version": item.version,
                        "path": str(path),
                        "replacement_version": highest_items[0].version,
                        "replacement_path": str(_resolved(highest_items[0].path)),
                    }
                )
            else:
                keep.append(
                    {
                        "name": item.name,
                        "version": item.version,
                        "path": str(path),
                        "reason": "newest-installed-version",
                    }
                )

    return {
        "scanned_roots": scanned_roots,
        "remove": sorted(remove, key=lambda item: (item["name"].casefold(), item["version"], item["path"])),
        "keep": sorted(keep, key=lambda item: (item["name"].casefold(), item["version"], item["path"])),
        "skipped": sorted(skipped, key=lambda item: (item["name"].casefold(), item["version"], item["path"])),
    }


def apply_prune(plan: dict[str, object], roots: Iterable[Path]) -> list[dict[str, str]]:
    allowed_roots = [_resolved(root) for root in roots]
    removed: list[dict[str, str]] = []
    for item in plan.get("remove", []):
        assert isinstance(item, dict)
        path = _resolved(Path(str(item["path"])))
        if path in allowed_roots or not any(_is_within(path, root) for root in allowed_roots):
            raise RuntimeError(f"refusing to remove path outside a managed plugin root: {path}")
        if path.is_symlink():
            raise RuntimeError(f"refusing to remove symlinked plugin directory: {path}")
        if path.exists():
            shutil.rmtree(path)
        removed.append({key: str(value) for key, value in item.items()})
    return removed


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Remove stale local plugin install directories only when a newer SemVer of the same plugin is present."
    )
    parser.add_argument("--root", action="append", default=[], help="Managed plugin install root. May be repeated.")
    parser.add_argument("--repo-root", help="Canonical repository root; source trees under it are never deleted.")
    parser.add_argument("--apply", action="store_true", help="Actually delete stale installed plugin directories.")
    parser.add_argument("--log", help="Optional path to persist the JSON result for startup/task verification.")
    parser.add_argument("--max-depth", type=int, default=2, help="Discovery depth below each managed root.")
    args = parser.parse_args(argv)

    roots = [_resolved(Path(value)) for value in args.root] if args.root else default_roots()
    plan = plan_prune(roots, repo_root=Path(args.repo_root) if args.repo_root else None)
    plan["apply"] = bool(args.apply)
    if args.apply:
        plan["removed"] = apply_prune(plan, roots)
    else:
        plan["removed"] = []

    rendered = json.dumps(plan, indent=2, sort_keys=True)
    if args.log:
        log_path = _resolved(Path(args.log))
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_path.write_text(rendered + "\n", encoding="utf-8")
    if sys.stdout is not None:
        print(rendered)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
