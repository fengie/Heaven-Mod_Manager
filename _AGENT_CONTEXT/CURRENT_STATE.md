# v8.8.68 selector acceptance closed — #558 reconciliation active

Issue #556 is **DONE**.

PR #567 merged v8.8.68 as `7def58c1b16d115e1555738ebad51717d1c1f752`. The exact source then passed hosted Windows verification run `36978710736` with 26/26 stages and produced updater build 382 / `MHW-Manual-Mod-Manager-v8.8.68-win-x64.zip` with SHA-256 `E11BB2CF34808A63C0922B85A1391865D17BB1F6EB1EB32FF79FAE91842166CA`.

Updater Installed Client E2E run `36979261045` passed the full packaged-client boundary:

- real update to build 382 / source `7def58c1b16d115e1555738ebad51717d1c1f752`;
- selected game exposed on the ComboBox automation peer as `Updater E2E Fake Game`;
- Switch enabled;
- Settings enabled;
- confirmed update journal;
- rollback scenario PASS with the previous build/source restored.

Evidence is persisted on canonical main in `_AGENT_CONTEXT/EVIDENCE/v8.8.68-heaven-windows-closure.log` and `_AGENT_CONTEXT/EVIDENCE/updater-installed-client-e2e-v8.8.68.log`. Issue #556 is closed as completed.

## Preserved selector contract

Do not regress:

- `AutomationProperties.Name="Active game"`;
- `AutomationProperties.AutomationId="ActiveGameSelector"`;
- ComboBox peer `AutomationProperties.ItemStatus` bound to `SelectedGame.DisplayName`;
- `TextSearch.TextPath="DisplayName"`;
- the visual selected presenter using the item template / human-readable DisplayName path;
- long-name character ellipsis;
- enabled Switch and Settings controls.

## Current active catalog work

Issue #558 remains ACTIVE. PR #565 contains the next provider-search tranche but was prepared on the pre-v8.8.68 line and carries v8.8.67 metadata. It is not merge-ready against current main.

The next integration must reconcile its semantic source/tests onto fresh main, preserve the closed #556 selector behavior, advance the product patch to v8.8.69, and require all exact-head repository gates before merge.

Ordinary per-keystroke Browse Mods filtering remains local to SQLite/FTS. Explicit remote search may contact only configured providers that advertise `CatalogProviderCapabilities.Search`; unsupported providers must not be probed.

## Routing

Current live Heaven Local Bridge heartbeats expose v8 workers on both hosts. `heaven2` remains the HMAC-authenticated control host; `heaven` is the heavy worker. Do not weaken the authentication boundary to force local-agent execution.
