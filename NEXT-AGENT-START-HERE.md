# v8.8.66 installed selector UI acceptance — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `fix/issue556-installed-ui-acceptance-v8.8.66-20261002`
Issue: #556
Parent canonical main at task start: `480e7efd0bf85b260594d8386e6162d39ec7b924`

## Objective

Prevent the escaped header selector regression from passing release validation again. v8.8.65 fixed the shared ComboBox selected-content template, but the previous acceptance only proved source/build/update behavior, not the rendered selected game text in the packaged client.

## v8.8.66 candidate

- The header game-name TextBlock now carries `AutomationProperties.AutomationId="ActiveGameDisplayName"` while retaining `TextTrimming="CharacterEllipsis"`.
- The integration test project enables WPF references and the real packaged updater E2E inspects the newly updated client's Windows UI Automation tree.
- Packaged acceptance requires:
  - the `Active game` ComboBox to exist;
  - its rendered `ActiveGameDisplayName` text to equal `Updater E2E Fake Game`;
  - no raw `GameProfile { ... }` text;
  - enabled Switch and Settings buttons.
- The updater E2E evidence verifier rejects missing/incorrect selector text or disabled adjacent actions and persists those fields in evidence.

## Verification state

Not yet integrated. Exact-head PR gates and the post-merge Windows release / installed-client E2E must still pass.

The mandatory local-offload route was attempted before mechanical implementation:
- bridge health job `chatgpt-20261002-043000-issue556-health-heaven2`;
- bridge health job `chatgpt-20261002-043000-issue556-health-heaven`.
Neither produced a status/result, and neither host heartbeat was readable from the relay. No local-agent execution or local build is claimed. Do not weaken HMAC/bridge controls to compensate.

## Unresolved risks

- **Unresolved risk:** UI Automation may expose the WPF DataTemplate differently on the self-hosted runner; exact-head CI is required to prove the test compiles and the packaged E2E can observe the rendered element.
- **Unresolved risk:** #556 is not DONE until the exact v8.8.66 release is published and the installed-client E2E passes the selector assertions.
- Live thumbnail visual acceptance remains separate from #556.

## Ordered next actions

1. Open the v8.8.66 PR and run Workflow Feature, Product Security, and Toolbox Ownership gates on one exact head.
2. Repair concrete failures only; do not weaken UI acceptance.
3. Refresh main/ownership, merge only when exact-head gates are green, and verify remote main.
4. Allow Windows Release Gate to publish the exact integrated v8.8.66 source without advancing release-relevant main while publication runs.
5. Require Updater Installed Client E2E to pass the new selector evidence checks.
6. Close #556 only after that packaged acceptance passes; then update canonical handoff/state without another product version bump.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; refresh claims/issues before mutation; preserve the continuity constitution and active learned rules; propagate the same obligation. **Do not break the chain.**
