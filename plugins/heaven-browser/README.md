# Heaven Browser

Reusable browser-on-desktop automation built from Heaven Bridge app/window/UIA primitives: launch a browser, navigate, locate/invoke accessible controls, fill non-secret fields, wait for windows, and capture screenshots.

This package is intentionally truthful about its boundary: it is **not yet a CDP/Playwright DOM/network/console adapter**. That deeper browser layer remains a next-step enhancement rather than being falsely advertised.

Validate with `python .\plugins\heaven-browser\verify.py`.
