# v8.8.67 selector UIA raw-tree hardening — candidate state

Issue #556 remains ACTIVE. v8.8.66 source/build/release succeeded, but packaged E2E #289 failed the new selector UI acceptance. This is retained as a real acceptance failure rather than being waived.

## Candidate change

- Stable automation ID on the active-game ComboBox.
- Explicit accessible name on the rendered DisplayName TextBlock.
- Raw UI Automation tree traversal for templated selector/display/button discovery.
- Bounded raw-tree failure diagnostics.
- Console emission of installed-client failure log/evidence even when artifact upload cannot run because storage quota is exhausted.

## Verification boundary

v8.8.67 has no green claim yet. Exact PR gates, exact release publication, and packaged updater E2E are all required before #556 can close.

## Routing

Heaven Local Bridge health probes for heaven2 and heaven were previously submitted under this issue and produced no durable status/result/heartbeat. No local-agent execution is claimed; bridge signing/HMAC controls remain intact.
