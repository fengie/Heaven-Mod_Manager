# v8.8.68 selector peer-value acceptance — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `fix/issue556-selector-automation-v8.8.68-20261002`
Issue: #556
Parent canonical main at task start: `658e5311f731f49eaf4d91dc0be55919260c9dfb`

## Why this tranche exists

v8.8.67 passed all exact-head PR gates and Windows Release Gate #381, publishing immutable `updater-main-381` from source `af73d3ce55b3bcbeb5d34184506829e57aca9cba`. Updater Installed Client E2E #290 then proved that the closed custom WPF ComboBox automation peer exposes no raw descendants. Therefore a child `ActiveGameDisplayName` probe cannot verify the selected value cross-process even though the XAML template is present.

E2E #290 attempt 1 also hit a transient GitHub release-asset HTTP 500. A single failed-job rerun cleared that transport failure and reached the deterministic selector failure; do not loop retries.

## v8.8.68 candidate

- Preserve `AutomationProperties.Name="Active game"` and `AutomationProperties.AutomationId="ActiveGameSelector"`.
- Bind `AutomationProperties.ItemStatus` to `SelectedGame.DisplayName` so the ComboBox automation peer itself exposes the selected human-readable value.
- Add `TextSearch.TextPath="DisplayName"` as a control-level text fallback.
- Keep the existing `DisplayName` ItemTemplate and character ellipsis.
- Make packaged E2E require a visible/bounded ComboBox, exact accessibility name, exact ItemStatus DisplayName, and enabled Switch/Settings.
- Keep the structural regression that rejects the old SelectionBoxItemTemplate/raw-object visual path.

## Verification state

No v8.8.68 green claim exists yet.

Last release evidence:
- v8.8.67 merge: `af73d3ce55b3bcbeb5d34184506829e57aca9cba`;
- Windows Release Gate #381: `36976475498`, success;
- immutable release: `updater-main-381`;
- package digest: `sha256:c1cc1fc9e62b41f5cc9d0a4020689b32a60d9c28daf4d0eedfd08c66729261dd`;
- Updater Installed Client E2E #290: attempt 1 transient HTTP 500; attempt 2 deterministic selector child-UIA failure.

Mandatory local Heaven offload was attempted earlier in this issue with both heaven2/heaven bridge health jobs; neither produced durable status/result/heartbeat. No local-agent verification is claimed and the HMAC boundary must not be weakened.

## Unresolved risks

- **Unresolved risk:** v8.8.68 still needs exact-head verification, exact release publication, and packaged ComboBox peer-value UI Automation proof before issue #556 can close.
- **Unresolved risk:** GitHub Actions artifact storage remains exhausted, so installed-client failures must remain diagnosable from console-emitted evidence.
- **Unresolved risk:** the release-asset API returned a transient HTTP 500 during E2E #290 attempt 1; one bounded rerun cleared it, but repeated transport failures must not be mistaken for product failures.

## Ordered continuation

1. Open the v8.8.68 PR from this branch.
2. Require all three exact-head PR gates and repair only concrete failures.
3. Refresh main/ownership and merge only the exact green head.
4. Freeze release-relevant main while Windows Release Gate publishes v8.8.68.
5. Require Updater Installed Client E2E to pass the ComboBox peer-value selector assertion, Switch/Settings, update, and rollback.
6. Close #556 only after packaged E2E success.
7. Reconcile #565/#558 after #556 closure and advance its patch to avoid version collision.

## Successor obligation

The successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` in full, bootstrap from current `fengie/heaven-toolbox@main` before current MHW `main`, and propagate this continuity obligation to the next agent. **Do not break the chain.**
