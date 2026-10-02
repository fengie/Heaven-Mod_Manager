# v8.8.66 installed selector UI acceptance — candidate state

Issue #556 remains ACTIVE. v8.8.65 fixed the source-level ComboBox selected presenter and published updater-main-379 successfully, but the escaped regression demonstrated that source/XAML verification alone is too weak for this UI boundary.

## Candidate work

- Add a stable UI Automation ID to the rendered active-game TextBlock.
- Extend the real packaged updater E2E to inspect the updated WPF process and require the active game's rendered display text.
- Detect raw `GameProfile { ... }` output in the selector automation subtree.
- Require Switch and Settings to remain enabled.
- Persist selector evidence and make the E2E workflow reject missing/incorrect evidence.

## Verification boundary

Candidate branch: `fix/issue556-installed-ui-acceptance-v8.8.66-20261002`.
No v8.8.66 green claim exists yet. Required PR gates, Windows release publication, and packaged updater E2E must attach to exact source before #556 can close.

## Routing

Heaven Local Bridge health probes for both heaven2 and heaven were submitted but produced no status/result and no readable host heartbeat. Local-agent offload is therefore unavailable for this turn; no local-agent/build result is claimed.
