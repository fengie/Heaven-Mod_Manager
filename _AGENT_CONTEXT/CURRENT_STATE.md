# v8.8.68 selector peer-value acceptance — candidate state

Issue #556 remains ACTIVE.

v8.8.67 source merged as `af73d3ce55b3bcbeb5d34184506829e57aca9cba`, Windows Release Gate #381 (`36976475498`) passed, and immutable `updater-main-381` published `MHW-Manual-Mod-Manager-v8.8.67-win-x64.zip` with digest `sha256:c1cc1fc9e62b41f5cc9d0a4020689b32a60d9c28daf4d0eedfd08c66729261dd`.

Updater Installed Client E2E #290 (`36976965567`) then produced two useful results:
- attempt 1 stopped before app launch on a transient GitHub release-asset HTTP 500; one bounded rerun cleared that external fault;
- attempt 2 reached the packaged client and failed deterministically because the closed WPF `ActiveGameSelector` ComboBox automation peer exposes zero raw descendants, so the templated `ActiveGameDisplayName` child is not observable cross-process.

## Candidate change

- Keep `AutomationProperties.Name="Active game"` and the stable `ActiveGameSelector` automation ID.
- Expose `SelectedGame.DisplayName` on the ComboBox peer through `AutomationProperties.ItemStatus`.
- Add `TextSearch.TextPath="DisplayName"` as a control-level human-readable text fallback.
- Make installed-client E2E assert the visible/bounded ComboBox peer, its stable accessibility name, its exact selected DisplayName, and enabled Switch/Settings actions.
- Retain the existing structural XAML guard that forces the closed selected presenter through the ItemTemplate/DisplayName path.

## Verification boundary

v8.8.68 has no green claim yet. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final head, then publish that exact source and require packaged updater update + selector + Switch/Settings + rollback acceptance before closing #556.

## Routing

Heaven Local Bridge health probes for heaven2 and heaven previously produced no durable status/result/heartbeat. No local-agent verification is claimed; signing/HMAC controls remain intact.
