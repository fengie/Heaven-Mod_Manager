# v8.8.67 selector UIA raw-tree hardening — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `fix/issue556-uia-diagnostics-v8.8.67-20261002`
Issue: #556
Parent canonical main at task start: `ca7978f5d8f6d420c9216ca7f83f7bdca09a8af1`

## Why this tranche exists

v8.8.66 successfully compiled, merged, and published as immutable updater-main-380, but Updater Installed Client E2E #289 (`36966823595`) failed in the new selector UI acceptance step. Package build, release publication, exact target resolution, and pre-acceptance updater flow all succeeded.

The failed test redirected its detailed xUnit/evidence output to files, while GitHub artifact upload then hit the repository Actions storage quota. The next acceptance layer therefore must both observe templated WPF content more robustly and emit diagnostics to the console before failing.

## v8.8.67 candidate

- Add `AutomationProperties.AutomationId="ActiveGameSelector"` to the header ComboBox.
- Keep `ActiveGameDisplayName` on the rendered TextBlock and explicitly bind its automation Name to `DisplayName`.
- Traverse `TreeWalker.RawViewWalker` for selector/display/button discovery instead of relying only on Control View descendants.
- On mismatch, report a bounded raw UIA subtree and preserve the existing raw-`GameProfile` rejection.
- Print installed-client E2E stdout/stderr plus `evidence.json` into the workflow console before throwing when the test process fails.
- Product version is 8.8.67 because the shipped WPF accessibility surface changed.

## Verification state

No v8.8.67 green claim exists yet. v8.8.66 remains the last published baseline:
- PR #563 merge: `5459db663f55388e72da97a79cb6e22ff0673048`;
- Windows Release Gate #380: `36966406969`, success;
- immutable updater-main-380 package digest: `sha256:4ac1be2a01eb78b70d304c758564eb888fcc169022f1f907885414fbe49d31a9`;
- Updater Installed Client E2E #289: `36966823595`, failed in selector UI acceptance.

Mandatory local Heaven offload was attempted earlier in this issue with both heaven2/heaven bridge health jobs; neither produced durable status/result/heartbeat. No local-agent verification is claimed and the HMAC boundary must not be weakened.

## Unresolved risks

- **Unresolved risk:** the raw WPF automation tree shape on the self-hosted session still needs exact packaged proof.
- **Unresolved risk:** #556 remains open until the exact v8.8.67 release passes packaged rendered-text, Switch/Settings, update, and rollback acceptance.
- Artifact storage quota remains exhausted, so console-first failure diagnostics are required.

## Ordered continuation

1. Open the v8.8.67 PR and require Workflow Feature, Product Security, and Toolbox Ownership gates on one exact head.
2. Repair only concrete compile/analyzer/test failures without weakening selector acceptance.
3. Refresh claim and main, merge only when exact-head gates are green.
4. Freeze release-relevant main while Windows Release Gate publishes v8.8.67.
5. Require Updater Installed Client E2E to pass the raw-tree selector assertions and evidence verification.
6. Only after packaged E2E success, close #556 and persist integrated evidence without another product patch bump.

## Successor obligation

The successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` in full, bootstrap from current `fengie/heaven-toolbox@main` before current MHW `main`, and must propagate this continuity obligation to the next agent. **Do not break the chain.**
