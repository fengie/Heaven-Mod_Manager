import sys
import tempfile
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(ROOT))

from heaven_browser.deep import PlaywrightDeepBrowser, _display_url, _safe_url


class _FakePage:
    def __init__(self, *, fail_navigation=False):
        self.fail_navigation=fail_navigation
        self.closed=False
        self.url="about:blank"

    def on(self, event, callback):
        return None

    def goto(self, url, **kwargs):
        if self.fail_navigation:
            raise RuntimeError("navigation failed")
        self.url=url

    def is_closed(self):
        return self.closed

    def close(self):
        self.closed=True

    def title(self):
        return ""


class _FakeContext:
    def __init__(self):
        self.pages=[]
        self.created=[]
        self.closed=False
        self.fail_next_navigation=False
        self._page_handler=None

    def on(self, event, callback):
        if event=="page":
            self._page_handler=callback

    def new_page(self):
        page=_FakePage(fail_navigation=self.fail_next_navigation)
        self.fail_next_navigation=False
        self.pages.append(page)
        self.created.append(page)
        if self._page_handler is not None:
            self._page_handler(page)
        return page

    def close(self):
        self.closed=True
        for page in self.pages:
            page.closed=True


class _FakeBrowser:
    def __init__(self, context):
        self.context=context
        self.closed=False

    def new_context(self, **kwargs):
        return self.context

    def close(self):
        self.closed=True


class _FakeChromium:
    def __init__(self, browser):
        self.browser=browser

    def launch(self, **kwargs):
        return self.browser


class _FakeRuntime:
    def __init__(self, browser):
        self.chromium=_FakeChromium(browser)


class _FakeManager:
    def __init__(self, browser):
        self.runtime=_FakeRuntime(browser)
        self.stopped=False

    def start(self):
        return self.runtime

    def stop(self):
        self.stopped=True


class DeepBrowserValidationTests(unittest.TestCase):
    def test_url_blocks_embedded_credentials_and_non_http(self):
        with self.assertRaises(ValueError):
            _safe_url("javascript:alert(1)")
        with self.assertRaises(ValueError):
            _safe_url("https://user:pass@example.com/")
        self.assertEqual("https://example.com/path?q=1",_safe_url("https://example.com/path?q=1"))

    def test_display_url_strips_query_and_fragment(self):
        self.assertEqual("https://example.com/path",_display_url("https://example.com/path?token=secret#frag"))

    def test_display_url_preserves_ipv6_brackets(self):
        self.assertEqual("http://[::1]:9222/a",_display_url("http://[::1]:9222/a?token=secret"))

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

    def test_create_session_navigation_failure_closes_owned_resources(self):
        with tempfile.TemporaryDirectory() as td:
            context=_FakeContext()
            context.fail_next_navigation=True
            browser=_FakeBrowser(context)
            manager=_FakeManager(browser)
            provider=PlaywrightDeepBrowser(td,playwright_factory=lambda:manager)
            with self.assertRaisesRegex(RuntimeError,"navigation failed"):
                provider.create_session("https://example.com")
            self.assertEqual({},provider._sessions)
            self.assertTrue(context.closed)
            self.assertTrue(browser.closed)

    def test_open_tab_navigation_failure_restores_previous_active_tab(self):
        with tempfile.TemporaryDirectory() as td:
            context=_FakeContext()
            browser=_FakeBrowser(context)
            manager=_FakeManager(browser)
            provider=PlaywrightDeepBrowser(td,playwright_factory=lambda:manager)
            created=provider.create_session("https://example.com")
            session=provider._sessions[created["session_id"]]
            original=created["tab_id"]
            context.fail_next_navigation=True
            with self.assertRaisesRegex(RuntimeError,"navigation failed"):
                provider.open_tab(created["session_id"],"https://invalid.example")
            self.assertEqual(original,session.active_tab)
            self.assertEqual([original],list(session.tabs))
            self.assertTrue(context.created[-1].closed)
            provider.close_session(created["session_id"])

    def test_tabs_forgets_externally_closed_active_tab(self):
        with tempfile.TemporaryDirectory() as td:
            context=_FakeContext()
            browser=_FakeBrowser(context)
            manager=_FakeManager(browser)
            provider=PlaywrightDeepBrowser(td,playwright_factory=lambda:manager)
            created=provider.create_session("https://example.com")
            session=provider._sessions[created["session_id"]]
            session.tabs[created["tab_id"]].closed=True
            self.assertEqual([],provider.tabs(created["session_id"]))
            self.assertIsNone(session.active_tab)
            self.assertEqual({},session.tabs)
            self.assertEqual({},session.page_ids)
            provider.close_session(created["session_id"])

    def test_external_closed_active_tab_is_forgotten(self):
        with tempfile.TemporaryDirectory() as td:
            context=_FakeContext()
            browser=_FakeBrowser(context)
            manager=_FakeManager(browser)
            provider=PlaywrightDeepBrowser(td,playwright_factory=lambda:manager)
            created=provider.create_session("https://example.com")
            session=provider._sessions[created["session_id"]]
            page=session.tabs[created["tab_id"]]
            page.closed=True
            with self.assertRaisesRegex(RuntimeError,"browser tab is closed"):
                provider._page(created["session_id"])
            self.assertIsNone(session.active_tab)
            self.assertEqual({},session.tabs)
            self.assertEqual({},session.page_ids)
            provider.close_session(created["session_id"])


if __name__=="__main__":
    unittest.main()
