---
name: heaven-desktop-control
description: Structured Windows desktop control on heaven through Heaven Local Bridge. Use for screenshots, displays, windows, cursor/mouse, keyboard/text, app launch and explicit guarded clipboard operations instead of Remote Desktop Commander.
---

# Heaven Desktop Control

Start with bridge `health` when current capability state matters and use only advertised actions.

Observation:
- `display_list`
- `window_list`
- `gui_cursor_get`
- `screenshot`

Interaction:
- `gui_mouse_move`, `gui_mouse_button`, `gui_mouse_click`, `gui_mouse_scroll`
- `gui_key`, `gui_type`
- `window_focus`, `window_move`, `window_state`, `window_close`
- `app_launch`
- `clipboard_write`
- `clipboard_read` only for explicit clipboard-read intent with `allow_relay:true`

Prefer HWND after `window_list`. Re-observe state after meaningful GUI mutations.

The relay is persisted transport. Never type or copy passwords, tokens, API keys, cookies, private keys or recovery codes through queue payloads, GUI text or clipboard actions.

Do not use Remote Desktop Commander unless the user explicitly authorizes it for the current request.
