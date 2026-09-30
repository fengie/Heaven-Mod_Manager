from __future__ import annotations

import collections
import pathlib
import urllib.parse
import uuid
from dataclasses import dataclass, field
from typing import Any, Callable, Mapping


def _safe_url(value: Any) -> str:
    if not isinstance(value, str) or not value or len(value) > 8192:
        raise ValueError("invalid URL")
    parsed = urllib.parse.urlsplit(value)
    if parsed.scheme not in {"http", "https"} or not parsed.netloc:
        raise ValueError("only http/https URLs are allowed")
    if parsed.username or parsed.password:
        raise ValueError("URLs must not contain embedded credentials")
    return value


def _display_url(value: str) -> str:
    parsed = urllib.parse.urlsplit(value)
    host = parsed.hostname or ""
    port = f":{parsed.port}" if parsed.port else ""
    path = parsed.path or "/"
    return urllib.parse.urlunsplit((parsed.scheme, host + port, path, "", ""))


@dataclass
class _Session:
    session_id: str
    browser: Any
    context: Any
    owned_browser: bool
    tabs: dict[str, Any] = field(default_factory=dict)
    page_ids: dict[int, str] = field(default_factory=dict)
    active_tab: str | None = None
    console_events: collections.deque[dict[str, Any]] = field(default_factory=lambda: collections.deque(maxlen=200))
    network_events: collections.deque[dict[str, Any]] = field(default_factory=lambda: collections.deque(maxlen=200))


class PlaywrightDeepBrowser:
    """Optional host-side Playwright provider for owned sessions and loopback CDP attach."""

    def __init__(
        self,
        allowed_download_root: str | pathlib.Path,
        *,
        max_events: int = 200,
        playwright_factory: Callable[[], Any] | None = None,
    ) -> None:
        if not isinstance(max_events, int) or not 10 <= max_events <= 2000:
            raise ValueError("max_events must be 10..2000")
        self.download_root = pathlib.Path(allowed_download_root).expanduser().resolve()
        self.download_root.mkdir(parents=True, exist_ok=True)
        self.max_events = max_events
        self._factory = playwright_factory
        self._manager: Any | None = None
        self._playwright: Any | None = None
        self._sessions: dict[str, _Session] = {}

    def _runtime(self) -> Any:
        if self._playwright is not None:
            return self._playwright
        if self._factory is None:
            try:
                from playwright.sync_api import sync_playwright
            except ImportError as exc:
                raise RuntimeError(
                    "deep browser provider requires the optional 'playwright' dependency"
                ) from exc
            manager = sync_playwright()
        else:
            manager = self._factory()
        self._manager = manager
        self._playwright = manager.start()
        return self._playwright

    @staticmethod
    def _endpoint(value: Any) -> str:
        if not isinstance(value, str) or len(value) > 2048:
            raise ValueError("invalid CDP endpoint")
        parsed = urllib.parse.urlsplit(value)
        if parsed.scheme not in {"http", "https"}:
            raise ValueError("CDP endpoint must use http/https")
        if parsed.username or parsed.password or parsed.query or parsed.fragment:
            raise ValueError("CDP endpoint must not contain credentials, query, or fragment")
        host = (parsed.hostname or "").lower()
        if host not in {"127.0.0.1", "localhost", "::1"}:
            raise ValueError("CDP attach is restricted to loopback")
        if parsed.port is None or not 1 <= parsed.port <= 65535:
            raise ValueError("CDP endpoint requires an explicit valid port")
        return value

    @staticmethod
    def _selector(value: Any) -> str:
        if not isinstance(value, str) or not value.strip() or len(value) > 2048:
            raise ValueError("selector must be a non-empty string up to 2048 characters")
        return value.strip()

    @staticmethod
    def _timeout(value: Any) -> int:
        if not isinstance(value, int) or not 100 <= value <= 120_000:
            raise ValueError("timeout_ms must be 100..120000")
        return value

    @staticmethod
    def _filename(value: Any, *, suffix: str | None = None) -> str:
        if not isinstance(value, str) or not value or len(value) > 255:
            raise ValueError("invalid filename")
        if pathlib.Path(value).name != value or value in {".", ".."} or "\x00" in value:
            raise ValueError("filename must not contain path segments")
        if suffix and not value.lower().endswith(suffix.lower()):
            raise ValueError(f"filename must end with {suffix}")
        return value

    def _destination(self, filename: str) -> pathlib.Path:
        filename = self._filename(filename)
        destination = (self.download_root / filename).resolve()
        if destination.parent != self.download_root:
            raise ValueError("destination escapes allowed download root")
        return destination

    def _session(self, session_id: Any) -> _Session:
        if not isinstance(session_id, str) or session_id not in self._sessions:
            raise KeyError("unknown browser session")
        return self._sessions[session_id]

    def _page(self, session_id: str, tab_id: str | None = None) -> tuple[_Session, Any, str]:
        session = self._session(session_id)
        resolved = tab_id or session.active_tab
        if not resolved or resolved not in session.tabs:
            raise KeyError("unknown browser tab")
        page = session.tabs[resolved]
        if getattr(page, "is_closed", lambda: False)():
            session.tabs.pop(resolved, None)
            raise RuntimeError("browser tab is closed")
        return session, page, resolved

    def _register_page(self, session: _Session, page: Any) -> str:
        existing = session.page_ids.get(id(page))
        if existing:
            session.active_tab = existing
            return existing
        tab_id = uuid.uuid4().hex[:16]
        session.tabs[tab_id] = page
        session.page_ids[id(page)] = tab_id
        session.active_tab = tab_id
        session.console_events = collections.deque(session.console_events, maxlen=self.max_events)
        session.network_events = collections.deque(session.network_events, maxlen=self.max_events)

        def on_console(message: Any) -> None:
            location = getattr(message, "location", {}) or {}
            session.console_events.append(
                {
                    "type": str(getattr(message, "type", "unknown"))[:32],
                    "url": _display_url(str(location.get("url") or "")) if location.get("url") else "",
                    "line": int(location.get("lineNumber") or 0),
                }
            )

        def on_request(request: Any) -> None:
            session.network_events.append(
                {
                    "event": "request",
                    "method": str(getattr(request, "method", ""))[:16],
                    "url": _display_url(str(getattr(request, "url", ""))),
                    "resource_type": str(getattr(request, "resource_type", ""))[:32],
                }
            )

        def on_response(response: Any) -> None:
            session.network_events.append(
                {
                    "event": "response",
                    "status": int(getattr(response, "status", 0)),
                    "url": _display_url(str(getattr(response, "url", ""))),
                }
            )

        page.on("console", on_console)
        page.on("request", on_request)
        page.on("response", on_response)
        return tab_id

    def _build_session(self, browser: Any, context: Any, *, owned_browser: bool) -> _Session:
        session = _Session(
            session_id=uuid.uuid4().hex,
            browser=browser,
            context=context,
            owned_browser=owned_browser,
        )
        session.console_events = collections.deque(maxlen=self.max_events)
        session.network_events = collections.deque(maxlen=self.max_events)
        self._sessions[session.session_id] = session
        context.on("page", lambda page: self._register_page(session, page))
        for page in list(getattr(context, "pages", [])):
            self._register_page(session, page)
        return session

    def create_session(
        self,
        url: str,
        *,
        headless: bool = True,
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        url = _safe_url(url)
        timeout_ms = self._timeout(timeout_ms)
        if not isinstance(headless, bool):
            raise ValueError("headless must be boolean")
        runtime = self._runtime()
        browser = runtime.chromium.launch(
            headless=headless,
            downloads_path=str(self.download_root),
        )
        context = browser.new_context(accept_downloads=True)
        session = self._build_session(browser, context, owned_browser=True)
        page = context.new_page()
        tab_id = self._register_page(session, page)
        page.goto(url, wait_until="domcontentloaded", timeout=timeout_ms)
        return {"session_id": session.session_id, "tab_id": tab_id, "owned": True, "url": _display_url(page.url)}

    def attach_cdp(self, endpoint: str) -> Mapping[str, Any]:
        endpoint = self._endpoint(endpoint)
        runtime = self._runtime()
        browser = runtime.chromium.connect_over_cdp(endpoint)
        contexts = list(browser.contexts)
        if not contexts:
            raise RuntimeError("CDP browser exposed no context")
        session = self._build_session(browser, contexts[0], owned_browser=False)
        return {"session_id": session.session_id, "owned": False, "tabs": self.tabs(session.session_id)}

    def navigate(
        self,
        session_id: str,
        url: str,
        *,
        tab_id: str | None = None,
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        url = _safe_url(url)
        _, page, resolved = self._page(session_id, tab_id)
        page.goto(url, wait_until="domcontentloaded", timeout=self._timeout(timeout_ms))
        return {"tab_id": resolved, "url": _display_url(page.url)}

    def query(
        self,
        session_id: str,
        selector: str,
        *,
        tab_id: str | None = None,
        limit: int = 20,
    ) -> Mapping[str, Any]:
        selector = self._selector(selector)
        if not isinstance(limit, int) or not 1 <= limit <= 100:
            raise ValueError("limit must be 1..100")
        _, page, resolved = self._page(session_id, tab_id)
        locator = page.locator(selector)
        count = locator.count()
        items = []
        for index in range(min(count, limit)):
            item = locator.nth(index)
            items.append(
                {
                    "index": index,
                    "visible": bool(item.is_visible()),
                    "enabled": bool(item.is_enabled()),
                    "tag": str(item.evaluate("(el) => el.tagName.toLowerCase()"))[:32],
                    "input_type": (item.get_attribute("type") or "")[:32],
                }
            )
        return {"tab_id": resolved, "selector": selector, "count": count, "truncated": count > limit, "items": items}

    def click(
        self,
        session_id: str,
        selector: str,
        *,
        tab_id: str | None = None,
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        selector = self._selector(selector)
        _, page, resolved = self._page(session_id, tab_id)
        page.locator(selector).click(timeout=self._timeout(timeout_ms))
        return {"tab_id": resolved, "selector": selector, "clicked": True}

    def fill(
        self,
        session_id: str,
        selector: str,
        text: str,
        *,
        tab_id: str | None = None,
        secret: bool = False,
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        selector = self._selector(selector)
        if secret:
            raise ValueError("secret form fill requires an opaque host-side secret handle and is not exposed here")
        if not isinstance(text, str) or len(text) > 10_000:
            raise ValueError("text invalid/too large")
        _, page, resolved = self._page(session_id, tab_id)
        locator = page.locator(selector)
        if (locator.get_attribute("type") or "").lower() == "password":
            raise ValueError("password fields are not fillable through relay-safe deep browser APIs")
        locator.fill(text, timeout=self._timeout(timeout_ms))
        return {"tab_id": resolved, "selector": selector, "filled": True, "characters": len(text)}

    def wait(
        self,
        session_id: str,
        selector: str,
        *,
        tab_id: str | None = None,
        state: str = "visible",
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        selector = self._selector(selector)
        if state not in {"attached", "detached", "visible", "hidden"}:
            raise ValueError("invalid wait state")
        _, page, resolved = self._page(session_id, tab_id)
        page.locator(selector).wait_for(state=state, timeout=self._timeout(timeout_ms))
        return {"tab_id": resolved, "selector": selector, "state": state, "matched": True}

    def tabs(self, session_id: str) -> list[dict[str, Any]]:
        session = self._session(session_id)
        result = []
        for tab_id, page in list(session.tabs.items()):
            if getattr(page, "is_closed", lambda: False)():
                session.tabs.pop(tab_id, None)
                continue
            title = ""
            try:
                title = str(page.title())[:512]
            except Exception:
                title = ""
            result.append({"tab_id": tab_id, "url": _display_url(str(page.url)), "title": title, "active": tab_id == session.active_tab})
        return result

    def open_tab(
        self,
        session_id: str,
        url: str,
        *,
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        url = _safe_url(url)
        session = self._session(session_id)
        page = session.context.new_page()
        tab_id = self._register_page(session, page)
        page.goto(url, wait_until="domcontentloaded", timeout=self._timeout(timeout_ms))
        return {"tab_id": tab_id, "url": _display_url(page.url)}

    def close_tab(self, session_id: str, tab_id: str) -> Mapping[str, Any]:
        session, page, resolved = self._page(session_id, tab_id)
        page.close()
        session.tabs.pop(resolved, None)
        session.page_ids.pop(id(page), None)
        if session.active_tab == resolved:
            session.active_tab = next(iter(session.tabs), None)
        return {"tab_id": resolved, "closed": True}

    def download(
        self,
        session_id: str,
        selector: str,
        *,
        tab_id: str | None = None,
        filename: str | None = None,
        timeout_ms: int = 30_000,
    ) -> Mapping[str, Any]:
        selector = self._selector(selector)
        _, page, resolved = self._page(session_id, tab_id)
        with page.expect_download(timeout=self._timeout(timeout_ms)) as info:
            page.locator(selector).click(timeout=self._timeout(timeout_ms))
        download = info.value
        chosen = filename or str(download.suggested_filename)
        destination = self._destination(self._filename(chosen))
        download.save_as(str(destination))
        return {"tab_id": resolved, "filename": destination.name, "path": str(destination), "url": _display_url(str(download.url))}

    def screenshot(
        self,
        session_id: str,
        filename: str,
        *,
        tab_id: str | None = None,
        full_page: bool = False,
    ) -> Mapping[str, Any]:
        filename = self._filename(filename, suffix=".png")
        if not isinstance(full_page, bool):
            raise ValueError("full_page must be boolean")
        _, page, resolved = self._page(session_id, tab_id)
        destination = self._destination(filename)
        page.screenshot(path=str(destination), full_page=full_page)
        return {"tab_id": resolved, "path": str(destination), "full_page": full_page}

    def console_summary(self, session_id: str, *, limit: int = 50) -> Mapping[str, Any]:
        session = self._session(session_id)
        if not isinstance(limit, int) or not 1 <= limit <= 200:
            raise ValueError("limit must be 1..200")
        events = list(session.console_events)[-limit:]
        counts: dict[str, int] = {}
        for event in session.console_events:
            key = str(event.get("type") or "unknown")
            counts[key] = counts.get(key, 0) + 1
        return {"counts": counts, "recent": events, "text_captured": False}

    def network_summary(self, session_id: str, *, limit: int = 50) -> Mapping[str, Any]:
        session = self._session(session_id)
        if not isinstance(limit, int) or not 1 <= limit <= 200:
            raise ValueError("limit must be 1..200")
        return {"recent": list(session.network_events)[-limit:], "query_strings_captured": False}

    def close_session(self, session_id: str) -> Mapping[str, Any]:
        session = self._session(session_id)
        self._sessions.pop(session_id, None)
        if session.owned_browser:
            try:
                session.context.close()
            finally:
                session.browser.close()
        return {"session_id": session_id, "closed": True, "owned_browser_closed": session.owned_browser}

    def shutdown(self) -> None:
        for session_id in list(self._sessions):
            self.close_session(session_id)
        if self._manager is not None:
            self._manager.stop()
        self._manager = None
        self._playwright = None
