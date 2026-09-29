---
name: heaven-desktop-control
description: Structured local Windows desktop control for the user's `heaven` worker through Heaven Local Bridge. Use for screenshots, displays, windows, cursor/mouse, keyboard/text, app launch, and explicit clipboard operations. Prefer these actions over Remote Desktop Commander or raw shell GUI automation.
---

# Heaven Local Desktop Control

Use the existing Heaven Local Bridge relay and worker. This skill extends, rather than replaces, the main `heaven-bridge` skill and its filesystem/process/reliability rules.

## Required routing

- Target machine: `heaven`.
- `heaven2` remains the control/credential authority.
- Do not use Remote Desktop Commander unless the user explicitly authorizes it for the current request.
- Start with `health` when current capability state matters. Only use desktop actions advertised by the live worker.
- Prefer structured desktop actions to PowerShell, CMD, Python GUI scripts, or ad-hoc automation.

## Observation actions

- `display_list`: enumerate displays and working areas.
- `window_list`: enumerate titled top-level windows with HWND, PID, visibility, minimized state, and bounds.
- `gui_cursor_get`: read current pointer coordinates.
- `screenshot`: capture `scope: primary`, `all`, or `monitor`; `monitor` requires `monitor_index`. The PNG stays local until explicitly retrieved with `fs_read_binary`.

## Interaction actions

- `gui_mouse_move`: move pointer to `x`,`y`; optional bounded `duration_ms`.
- `gui_mouse_button`: `button` = left/right/middle and `state` = down/up. Use down -> move -> up for drag operations.
- `gui_mouse_click`: click at current or supplied coordinates; `count` is bounded.
- `gui_mouse_scroll`: wheel input; `horizontal:true` for horizontal scrolling.
- `gui_key`: named key input with optional modifiers. Use for shortcuts and navigation.
- `gui_type`: Unicode text injection with Win32 SendInput.
- `window_focus`: restore/focus a window.
- `window_move`: move/resize a window.
- `window_state`: hide, normal, maximize, show, minimize, or restore.
- `window_close`: request normal WM_CLOSE, not process termination.
- `app_launch`: launch an executable with an argv list and no shell interpolation.
- `clipboard_write`: write Unicode text to the interactive clipboard.
- `clipboard_read`: only for an explicit user clipboard-read request and only with `params.allow_relay=true`.

Windows can be selected by `hwnd`, `pid`, or title substring. Prefer HWND after `window_list`. Ambiguous selectors fail closed unless `first_match:true` is explicitly appropriate.

## Safety and privacy invariants

The GitHub queue/result transport is private but persisted. Never put passwords, access tokens, API keys, cookies, private keys, recovery codes, or other secrets into `gui_type`, `clipboard_write`, queue params, results, logs, or controller state.

`clipboard_read` is privacy-sensitive because its returned text traverses the private relay. Do not use it speculatively. Require an explicit clipboard-read intent and set `allow_relay:true` only for that operation.

Do not use mouse/keyboard mutation just to prove capability. For validation, prefer non-destructive `health`, `display_list`, `window_list`, `gui_cursor_get`, and `screenshot`. Use interaction actions when they are necessary to complete the user's actual task.

## Completion

A desktop action is complete only when the authoritative result JSON reports success. Queue submission alone is not completion. For multi-step GUI work, re-observe state after meaningful mutations instead of assuming input succeeded.
