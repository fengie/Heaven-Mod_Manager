# Heaven Browser

Reusable browser automation with two deliberately separate layers:

- the existing Heaven Bridge/UIA layer launches Brave/Edge/Chrome/Firefox, navigates normal user windows, locates accessible controls, fills non-secret fields, waits for windows, and captures desktop screenshots;
- an optional host-side Playwright layer provides owned Chromium sessions plus **loopback-only** CDP attachment for an already-debuggable Chromium/Brave instance.

## Deep capabilities

The optional provider adds DOM selectors, click/fill/wait, tabs, bounded downloads, page screenshots, and metadata-only console/network summaries. Network query strings and console message text are not captured. Password inputs and explicit secret fills are refused until opaque host-side secret handles can be resolved without putting secret values in relay payloads.

CDP attachment is intentionally limited to `localhost`, `127.0.0.1`, or `::1` with an explicit port. Attached browsers are treated as user-owned: closing a deep session disconnects the plugin's ownership record but does not close the attached browser. Owned Playwright sessions are closed by the provider.

Downloads and screenshots are saved only beneath the provider's configured allowed root. Suggested download filenames are reduced to a single filename and path traversal is rejected.

## Install the optional provider

From this package directory:

```powershell
pip install -e ".[deep]"
python -m playwright install chromium
```

The package currently targets Playwright 1.63.x+ within major version 1. Browser binaries remain an explicit host install rather than being silently downloaded during plugin import.

## Capabilities

Legacy/UIA:
- `browser.open`
- `browser.navigate`
- `browser.find_control`
- `browser.invoke_control`
- `browser.type_control`
- `browser.screenshot`
- `browser.wait_window`

Deep provider:
- `browser.deep.status`
- `browser.session.create`
- `browser.session.attach_cdp`
- `browser.session.close`
- `browser.dom.query`
- `browser.dom.click`
- `browser.dom.fill`
- `browser.dom.wait`
- `browser.tab.list`
- `browser.tab.open`
- `browser.tab.close`
- `browser.download`
- `browser.deep.screenshot`
- `browser.console.summary`
- `browser.network.summary`

Validate with `python .\plugins\heaven-browser\verify.py`.
