from __future__ import annotations

import os
import re
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Mapping

from .protocol import ControlPlaneError, bounded_int, require_mapping, require_string

_DEFAULT_IGNORED_DIRS = frozenset({
    ".git", ".hg", ".svn", ".idea", ".vscode", "__pycache__", ".pytest_cache",
    ".mypy_cache", ".ruff_cache", ".tox", ".venv", "venv", "node_modules",
    "dist", "build", "out", "coverage", ".next", ".turbo",
})
_SECRET_NAMES = frozenset({
    ".env", ".env.local", ".env.production", ".env.development",
    "credentials.json", "secrets.json", "id_rsa", "id_ed25519",
})
_SECRET_SUFFIXES = (".pem", ".key", ".p12", ".pfx", ".kdbx")
_TEXT_SUFFIXES = frozenset({
    ".py", ".pyi", ".js", ".jsx", ".ts", ".tsx", ".mjs", ".cjs",
    ".java", ".kt", ".kts", ".cs", ".cpp", ".cc", ".cxx", ".c", ".h", ".hpp",
    ".rs", ".go", ".rb", ".php", ".swift", ".scala", ".sh", ".ps1", ".bat", ".cmd",
    ".html", ".htm", ".css", ".scss", ".sass", ".less", ".vue", ".svelte",
    ".json", ".jsonl", ".yaml", ".yml", ".toml", ".ini", ".cfg", ".conf", ".xml",
    ".md", ".mdx", ".rst", ".txt", ".sql", ".graphql", ".gql",
})
_SYMBOL_PATTERNS = (
    re.compile(r"^\s*(?:async\s+)?def\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("),
    re.compile(r"^\s*class\s+([A-Za-z_][A-Za-z0-9_]*)\b"),
    re.compile(r"^\s*(?:export\s+)?(?:async\s+)?function\s+([A-Za-z_$][A-Za-z0-9_$]*)\s*\("),
    re.compile(r"^\s*(?:export\s+)?(?:class|interface|type|enum)\s+([A-Za-z_$][A-Za-z0-9_$]*)\b"),
    re.compile(
        r"^\s*(?:public|private|protected|internal|static|sealed|abstract|partial|\s)*"
        r"(?:class|interface|struct|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)\b"
    ),
)
_SECRET_ASSIGNMENT = re.compile(
    r"(?i)((?:^|[^A-Za-z0-9])(?:[A-Za-z0-9]+[_-])*(?:password|passwd|token|secret|api[_-]?key|private[_-]?key|authorization|cookie)"
    r"(?:[_-][A-Za-z0-9]+)*\s*[:=]\s*)['\"]?[^\s,'\"]{8,}['\"]?",
    re.MULTILINE,
)
_SECRET_VALUE_PATTERNS = (
    re.compile(r"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{8,}"),
    re.compile(r"\bgh[pousr]_[A-Za-z0-9_]{10,}\b"),
)


@dataclass(frozen=True)
class _IndexedFile:
    path: str
    text: str
    size_bytes: int
    mtime_ns: int


def _redact(text: str) -> str:
    out = _SECRET_ASSIGNMENT.sub(lambda match: f"{match.group(1)}<redacted>", text)
    for pattern in _SECRET_VALUE_PATTERNS:
        out = pattern.sub("<redacted>", out)
    return out


def _safe_relative_path(value: Any, field: str = "path_prefix") -> str:
    if value in (None, ""):
        return ""
    raw = require_string(value, field, max_length=4096).replace("\\", "/")
    if raw.startswith("/") or re.match(r"^[A-Za-z]:", raw):
        raise ControlPlaneError("INVALID_PATH_PREFIX", f"{field} must be repository-relative")
    parts = [part for part in raw.split("/") if part not in ("", ".")]
    if any(part == ".." for part in parts):
        raise ControlPlaneError("PATH_TRAVERSAL", f"{field} must not contain '..'")
    return "/".join(parts)


class RepositoryIndex:
    """Bounded, in-memory repository text and symbol index."""

    def __init__(
        self,
        root: str | os.PathLike[str],
        *,
        max_files: int = 20_000,
        max_file_bytes: int = 1_000_000,
        max_total_bytes: int = 64_000_000,
        ignored_dirs: Iterable[str] | None = None,
    ):
        try:
            resolved = Path(root).expanduser().resolve(strict=True)
        except (OSError, RuntimeError) as exc:
            raise ControlPlaneError("INDEX_ROOT_INVALID", "repository root does not exist") from exc
        if not resolved.is_dir():
            raise ControlPlaneError("INDEX_ROOT_INVALID", "repository root must be a directory")
        if not 1 <= max_files <= 100_000:
            raise ValueError("max_files out of range")
        if not 1 <= max_file_bytes <= 10_000_000:
            raise ValueError("max_file_bytes out of range")
        if not 1 <= max_total_bytes <= 512_000_000:
            raise ValueError("max_total_bytes out of range")

        self.root = resolved
        self.max_files = max_files
        self.max_file_bytes = max_file_bytes
        self.max_total_bytes = max_total_bytes
        self.ignored_dirs = frozenset(ignored_dirs or _DEFAULT_IGNORED_DIRS)
        self._files: dict[str, _IndexedFile] = {}
        self._skipped: dict[str, int] = {}
        self._generation = 0
        self._lock = threading.RLock()

    def _inside_root(self, path: Path) -> bool:
        try:
            path.relative_to(self.root)
            return True
        except ValueError:
            return False

    @staticmethod
    def _secret_name(name: str) -> bool:
        lowered = name.lower()
        return (
            lowered in _SECRET_NAMES
            or lowered.startswith(".env.")
            or lowered.endswith(_SECRET_SUFFIXES)
        )

    @staticmethod
    def _text_candidate(path: Path) -> bool:
        return path.suffix.lower() in _TEXT_SUFFIXES or path.name.lower() in {
            "dockerfile", "makefile", "justfile", "procfile", "license", "readme",
        }

    def refresh(self) -> dict[str, Any]:
        indexed: dict[str, _IndexedFile] = {}
        skipped = {
            "ignored_directory": 0,
            "secret_filename": 0,
            "symlink_escape": 0,
            "unsupported_type": 0,
            "oversized_file": 0,
            "decode_error": 0,
            "file_limit": 0,
            "byte_limit": 0,
            "stat_error": 0,
        }
        total_bytes = 0
        truncated = False

        for dirpath, dirnames, filenames in os.walk(self.root, followlinks=False):
            current = Path(dirpath)
            kept_dirs: list[str] = []
            for dirname in dirnames:
                candidate = current / dirname
                if dirname in self.ignored_dirs:
                    skipped["ignored_directory"] += 1
                    continue
                try:
                    resolved = candidate.resolve(strict=False)
                except (OSError, RuntimeError):
                    skipped["stat_error"] += 1
                    continue
                if candidate.is_symlink() and not self._inside_root(resolved):
                    skipped["symlink_escape"] += 1
                    continue
                kept_dirs.append(dirname)
            dirnames[:] = kept_dirs

            for filename in filenames:
                if len(indexed) >= self.max_files:
                    skipped["file_limit"] += 1
                    truncated = True
                    break
                path = current / filename
                rel = path.relative_to(self.root).as_posix()
                if self._secret_name(filename):
                    skipped["secret_filename"] += 1
                    continue
                if not self._text_candidate(path):
                    skipped["unsupported_type"] += 1
                    continue
                try:
                    resolved = path.resolve(strict=True)
                    if not self._inside_root(resolved):
                        skipped["symlink_escape"] += 1
                        continue
                    stat = resolved.stat()
                except (OSError, RuntimeError):
                    skipped["stat_error"] += 1
                    continue
                if stat.st_size > self.max_file_bytes:
                    skipped["oversized_file"] += 1
                    continue
                if total_bytes + stat.st_size > self.max_total_bytes:
                    skipped["byte_limit"] += 1
                    truncated = True
                    break
                try:
                    raw = resolved.read_bytes()
                    if b"\x00" in raw[:8192]:
                        skipped["unsupported_type"] += 1
                        continue
                    text = raw.decode("utf-8")
                except (OSError, UnicodeDecodeError):
                    skipped["decode_error"] += 1
                    continue
                total_bytes += len(raw)
                indexed[rel] = _IndexedFile(rel, text, len(raw), stat.st_mtime_ns)
            if truncated:
                break

        with self._lock:
            self._files = indexed
            self._skipped = skipped
            self._generation += 1
            generation = self._generation

        return {
            "generation": generation,
            "root": str(self.root),
            "indexed_files": len(indexed),
            "indexed_bytes": total_bytes,
            "skipped": dict(skipped),
            "truncated": truncated,
        }

    def stats(self) -> dict[str, Any]:
        with self._lock:
            return {
                "generation": self._generation,
                "root": str(self.root),
                "indexed_files": len(self._files),
                "indexed_bytes": sum(item.size_bytes for item in self._files.values()),
                "skipped": dict(self._skipped),
            }

    def _snapshot(self) -> tuple[int, list[_IndexedFile]]:
        with self._lock:
            return self._generation, list(self._files.values())

    @staticmethod
    def _matches_prefix(path: str, prefix: str) -> bool:
        return not prefix or path == prefix or path.startswith(prefix.rstrip("/") + "/")

    def search_text(
        self,
        query: Any,
        *,
        path_prefix: Any = "",
        offset: Any = 0,
        length: Any = 50,
        context_lines: Any = 2,
        case_sensitive: bool = False,
    ) -> dict[str, Any]:
        needle = require_string(query, "query", max_length=1024)
        prefix = _safe_relative_path(path_prefix)
        start = bounded_int(offset, "offset", default=0, minimum=0, maximum=1_000_000)
        page_size = bounded_int(length, "length", default=50, minimum=1, maximum=200)
        context = bounded_int(context_lines, "context_lines", default=2, minimum=0, maximum=10)
        if not isinstance(case_sensitive, bool):
            raise ControlPlaneError("INVALID_INPUT", "case_sensitive must be a boolean")

        generation, files = self._snapshot()
        matches: list[dict[str, Any]] = []
        target = needle if case_sensitive else needle.casefold()

        for item in sorted(files, key=lambda value: value.path):
            if not self._matches_prefix(item.path, prefix):
                continue
            lines = item.text.splitlines()
            for index, line in enumerate(lines):
                haystack = line if case_sensitive else line.casefold()
                if target not in haystack:
                    continue
                lo = max(0, index - context)
                hi = min(len(lines), index + context + 1)
                snippet = "\n".join(lines[lo:hi])[:4096]
                matches.append({
                    "path": item.path,
                    "line": index + 1,
                    "snippet_start_line": lo + 1,
                    "snippet": _redact(snippet),
                })
                if len(matches) >= start + page_size + 1:
                    break
            if len(matches) >= start + page_size + 1:
                break

        page = matches[start:start + page_size]
        return {
            "generation": generation,
            "query": needle,
            "offset": start,
            "length": len(page),
            "has_more": len(matches) > start + page_size,
            "items": page,
        }

    def search_symbols(
        self,
        query: Any,
        *,
        path_prefix: Any = "",
        offset: Any = 0,
        length: Any = 50,
    ) -> dict[str, Any]:
        needle = require_string(query, "query", max_length=256).casefold()
        prefix = _safe_relative_path(path_prefix)
        start = bounded_int(offset, "offset", default=0, minimum=0, maximum=1_000_000)
        page_size = bounded_int(length, "length", default=50, minimum=1, maximum=200)
        generation, files = self._snapshot()
        matches: list[dict[str, Any]] = []

        for item in sorted(files, key=lambda value: value.path):
            if not self._matches_prefix(item.path, prefix):
                continue
            for line_no, line in enumerate(item.text.splitlines(), 1):
                symbol = None
                for pattern in _SYMBOL_PATTERNS:
                    found = pattern.match(line)
                    if found:
                        symbol = found.group(1)
                        break
                if symbol is None or needle not in symbol.casefold():
                    continue
                matches.append({
                    "path": item.path,
                    "line": line_no,
                    "symbol": symbol,
                    "preview": _redact(line.strip()[:1024]),
                })
                if len(matches) >= start + page_size + 1:
                    break
            if len(matches) >= start + page_size + 1:
                break

        page = matches[start:start + page_size]
        return {
            "generation": generation,
            "query": str(query),
            "offset": start,
            "length": len(page),
            "has_more": len(matches) > start + page_size,
            "items": page,
        }


class IndexCapabilityProvider:
    """Standalone provider ready to be composed into the shared service dispatcher."""

    CAPABILITIES = frozenset({
        "index.refresh",
        "index.stats",
        "index.search.text",
        "index.search.symbols",
    })

    def __init__(self, index: RepositoryIndex):
        self.index = index

    def invoke(
        self,
        capability_name: str,
        payload: Mapping[str, Any] | None = None,
    ) -> dict[str, Any]:
        data = require_mapping({} if payload is None else payload)
        if capability_name == "index.refresh":
            if data:
                raise ControlPlaneError("INVALID_INPUT", "index.refresh takes no input")
            return self.index.refresh()
        if capability_name == "index.stats":
            if data:
                raise ControlPlaneError("INVALID_INPUT", "index.stats takes no input")
            return self.index.stats()
        if capability_name == "index.search.text":
            return self.index.search_text(
                data.get("query"),
                path_prefix=data.get("path_prefix", ""),
                offset=data.get("offset", 0),
                length=data.get("length", 50),
                context_lines=data.get("context_lines", 2),
                case_sensitive=data.get("case_sensitive", False),
            )
        if capability_name == "index.search.symbols":
            return self.index.search_symbols(
                data.get("query"),
                path_prefix=data.get("path_prefix", ""),
                offset=data.get("offset", 0),
                length=data.get("length", 50),
            )
        raise ControlPlaneError(
            "UNKNOWN_CAPABILITY",
            f"unknown indexing capability: {capability_name}",
        )
