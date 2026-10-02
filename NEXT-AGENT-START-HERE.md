# v8.8.68 selector closure — #558 continuation handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical source: `7def58c1b16d115e1555738ebad51717d1c1f752`
Evidence-bearing main: `601496093430214791395ba1bfcfadd3b0262ad2`
Closed issue: #556
Next active issue: #558
Open continuation PR: #565 (`agent/issue-558-provider-search-v8.8.67-20261002`)

## #556 closure

The header-selector regression is closed by exact packaged-client evidence, not by source inspection alone.

- PR #567 merged v8.8.68 as `7def58c1b16d115e1555738ebad51717d1c1f752`.
- Hosted Windows verification run `36978710736` passed 26/26 stages for that exact source and produced updater build 382.
- Release artifact: `MHW-Manual-Mod-Manager-v8.8.68-win-x64.zip`.
- Artifact SHA-256: `E11BB2CF34808A63C0922B85A1391865D17BB1F6EB1EB32FF79FAE91842166CA`.
- Updater Installed Client E2E run `36979261045` passed real update, selector acceptance, enabled Switch, enabled Settings, and rollback.
- The packaged selector exposed `selectorDisplayText = "Updater E2E Fake Game"`, proving the ComboBox peer carries the human-readable selected DisplayName cross-process.
- Evidence is persisted at `_AGENT_CONTEXT/EVIDENCE/v8.8.68-heaven-windows-closure.log` and `_AGENT_CONTEXT/EVIDENCE/updater-installed-client-e2e-v8.8.68.log`.
- Issue #556 is closed as completed.

Preserve the v8.8.68 selector contract: stable `ActiveGameSelector` automation identity, `AutomationProperties.ItemStatus` bound to `SelectedGame.DisplayName`, `TextSearch.TextPath="DisplayName"`, the visual ItemTemplate regression, readable ellipsis, and enabled Switch/Settings behavior.

## Current continuation

PR #565 carries the next #558 provider-search tranche but was authored against the pre-v8.8.68 line and still advertises v8.8.67 metadata. Treat it as semantic source to reconcile, not as merge-ready state.

Next work must:

1. refresh current `main`, #558 ownership, and PR #565;
2. preserve the provider-capability boundary: explicit remote search contacts only providers that advertise `CatalogProviderCapabilities.Search`; ordinary per-keystroke filtering remains local to SQLite/FTS;
3. reconcile only still-useful #565 source/tests onto fresh main while preserving the now-closed #556 selector behavior;
4. advance the reconciled product tranche to the next available patch (v8.8.69) with synchronized `VERSION.txt`, README, CHANGELOG, and continuity metadata;
5. require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final head before integration;
6. after integration, continue #558 with provider-aware pagination/browse expansion and deterministic scale/performance coverage, then #559 UX work.

## Routing and safety

Current Heaven Local Bridge host heartbeats show v8 workers on both `heaven2` and `heaven`. The `heaven2` control path remains HMAC-authenticated. Do not weaken or bypass that signing boundary merely to gain a local-agent route. Heavy execution belongs on `heaven`; operator/control actions belong on `heaven2`.

## Successor obligation

The successor must bootstrap from current `fengie/heaven-toolbox@main`, refresh current MHW `main`, read and preserve `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant learned rules, and propagate this continuity obligation to the next agent. **Do not break the chain.**
