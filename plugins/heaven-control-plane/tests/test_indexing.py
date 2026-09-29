from __future__ import annotations

import os
import sys
import tempfile
import threading
import unittest
from pathlib import Path

PLUGIN_ROOT = Path(__file__).resolve().parents[1]
if str(PLUGIN_ROOT) not in sys.path:
    sys.path.insert(0, str(PLUGIN_ROOT))

from heaven_control_plane.indexing import IndexCapabilityProvider, RepositoryIndex
from heaven_control_plane.protocol import ControlPlaneError


class RepositoryIndexTests(unittest.TestCase):
    def make_repo(self):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        (root / "src").mkdir()
        (root / "src" / "alpha.py").write_text(
            "class Alpha:\n"
            "    pass\n\n"
            "def helper():\n"
            "    token = 'safevalue'\n"
            "    return 1\n",
            encoding="utf-8",
        )
        (root / "src" / "beta.ts").write_text(
            "export function helperThing() { return 2 }\n",
            encoding="utf-8",
        )
        (root / ".env").write_text(
            "API_TOKEN=supersecretvalue\n",
            encoding="utf-8",
        )
        (root / "blob.bin").write_bytes(b"\x00abc")
        return temp, root

    def test_refresh_indexes_text_and_skips_secret_and_binary_files(self):
        temp, root = self.make_repo()
        try:
            index = RepositoryIndex(root)
            stats = index.refresh()

            self.assertEqual(stats["indexed_files"], 2)
            self.assertGreaterEqual(stats["skipped"]["secret_filename"], 1)
            self.assertGreaterEqual(stats["skipped"]["unsupported_type"], 1)
            self.assertFalse(stats["truncated"])

            text = index.search_text("helper")
            self.assertEqual(len(text["items"]), 2)
            self.assertEqual(
                {item["path"] for item in text["items"]},
                {"src/alpha.py", "src/beta.ts"},
            )

            symbols = index.search_symbols("helper")
            self.assertEqual(
                {item["symbol"] for item in symbols["items"]},
                {"helper", "helperThing"},
            )
        finally:
            temp.cleanup()

    def test_search_rejects_path_traversal_and_absolute_prefixes(self):
        temp, root = self.make_repo()
        try:
            index = RepositoryIndex(root)
            index.refresh()

            with self.assertRaises(ControlPlaneError) as traversal:
                index.search_text("x", path_prefix="../outside")
            self.assertEqual(traversal.exception.code, "PATH_TRAVERSAL")

            with self.assertRaises(ControlPlaneError) as absolute:
                index.search_text("x", path_prefix="C:/outside")
            self.assertEqual(absolute.exception.code, "INVALID_PATH_PREFIX")
        finally:
            temp.cleanup()

    def test_search_redacts_secret_like_values_from_snippets(self):
        temp, root = self.make_repo()
        try:
            (root / "src" / "secretish.py").write_text(
                "value = 'x'\n"
                "API_TOKEN=abcdefgh12345678\n"
                "Authorization: Bearer abcdefgh12345678\n",
                encoding="utf-8",
            )

            index = RepositoryIndex(root)
            index.refresh()
            result = index.search_text("API_TOKEN")

            self.assertEqual(len(result["items"]), 1)
            rendered = result["items"][0]["snippet"]
            self.assertIn("API_TOKEN=<redacted>", rendered)
            self.assertNotIn("abcdefgh12345678", rendered)
        finally:
            temp.cleanup()

    def test_search_pagination_is_bounded_and_deterministic(self):
        temp, root = self.make_repo()
        try:
            (root / "many.txt").write_text(
                "\n".join(["needle"] * 5),
                encoding="utf-8",
            )
            index = RepositoryIndex(root)
            index.refresh()

            first = index.search_text("needle", length=2, context_lines=0)
            second = index.search_text(
                "needle",
                offset=2,
                length=2,
                context_lines=0,
            )

            self.assertEqual(len(first["items"]), 2)
            self.assertTrue(first["has_more"])
            self.assertEqual(len(second["items"]), 2)
            self.assertTrue(second["has_more"])
            self.assertEqual(
                [item["line"] for item in first["items"]],
                [1, 2],
            )
            self.assertEqual(
                [item["line"] for item in second["items"]],
                [3, 4],
            )
        finally:
            temp.cleanup()

    def test_oversized_file_is_skipped_and_provider_validates_inputs(self):
        temp, root = self.make_repo()
        try:
            (root / "big.txt").write_text("x" * 101, encoding="utf-8")
            index = RepositoryIndex(root, max_file_bytes=100)
            stats = index.refresh()
            self.assertGreaterEqual(stats["skipped"]["oversized_file"], 1)

            provider = IndexCapabilityProvider(index)
            self.assertEqual(
                provider.invoke("index.stats")["indexed_files"],
                2,
            )
            self.assertEqual(
                provider.invoke(
                    "index.search.symbols",
                    {"query": "Alpha"},
                )["items"][0]["symbol"],
                "Alpha",
            )

            with self.assertRaises(ControlPlaneError) as invalid:
                provider.invoke("index.refresh", {"unexpected": True})
            self.assertEqual(invalid.exception.code, "INVALID_INPUT")
        finally:
            temp.cleanup()

    def test_symlink_escape_is_not_indexed_when_symlinks_are_available(self):
        temp, root = self.make_repo()
        outside = tempfile.TemporaryDirectory()
        try:
            target = Path(outside.name) / "outside.py"
            target.write_text(
                "def escaped():\n"
                "    pass\n",
                encoding="utf-8",
            )
            link = root / "src" / "escape.py"
            try:
                os.symlink(target, link)
            except (OSError, NotImplementedError):
                self.skipTest("symlink creation is unavailable")

            index = RepositoryIndex(root)
            stats = index.refresh()

            self.assertGreaterEqual(stats["skipped"]["symlink_escape"], 1)
            self.assertEqual(
                index.search_symbols("escaped")["items"],
                [],
            )
        finally:
            temp.cleanup()
            outside.cleanup()

    def test_refresh_and_search_can_race_without_partial_state_exposure(self):
        temp, root = self.make_repo()
        try:
            index = RepositoryIndex(root)
            index.refresh()
            failures = []

            def reader():
                try:
                    for _ in range(25):
                        result = index.search_text("helper", length=10)
                        if result["generation"] < 1:
                            failures.append("invalid generation")
                except Exception as exc:  # pragma: no cover - diagnostic path
                    failures.append(repr(exc))

            readers = [threading.Thread(target=reader) for _ in range(4)]
            for thread in readers:
                thread.start()
            for _ in range(5):
                index.refresh()
            for thread in readers:
                thread.join()

            self.assertEqual(failures, [])
            self.assertGreaterEqual(index.stats()["generation"], 6)
        finally:
            temp.cleanup()


if __name__ == "__main__":
    unittest.main()
