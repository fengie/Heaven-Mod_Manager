import sys
import tempfile
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))

from heaven_browser.deep import PlaywrightDeepBrowser, _display_url, _safe_url


class DeepBrowserValidationTests(unittest.TestCase):
    def test_url_blocks_embedded_credentials_and_non_http(self):
        with self.assertRaises(ValueError):
            _safe_url("javascript:alert(1)")
        with self.assertRaises(ValueError):
            _safe_url("https://user:pass@example.com/")
        self.assertEqual("https://example.com/path?q=1",_safe_url("https://example.com/path?q=1"))

    def test_display_url_strips_query_and_fragment(self):
        self.assertEqual("https://example.com/path",_display_url("https://example.com/path?token=secret#frag"))

    def test_cdp_attach_is_loopback_only(self):
        with tempfile.TemporaryDirectory() as td:
            provider=PlaywrightDeepBrowser(td)
            self.assertEqual("http://127.0.0.1:9222",provider._endpoint("http://127.0.0.1:9222"))
            with self.assertRaises(ValueError):
                provider._endpoint("https://example.com:9222")
            with self.assertRaises(ValueError):
                provider._endpoint("http://localhost:9222/?token=x")

    def test_download_destination_rejects_path_segments(self):
        with tempfile.TemporaryDirectory() as td:
            provider=PlaywrightDeepBrowser(td)
            with self.assertRaises(ValueError):
                provider._destination("../outside.bin")
            path=provider._destination("safe.bin")
            self.assertEqual(Path(td).resolve(),path.parent)


if __name__=="__main__":
    unittest.main()
