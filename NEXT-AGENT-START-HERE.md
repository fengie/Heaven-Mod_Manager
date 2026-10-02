# v8.8.74 updater E2E supersession classification — canonical-ready handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Integrated predecessor: v8.8.73
Change set: issue #587 updater installed-client E2E supersession classification

## v8.8.74 behavior

- `Updater Installed Client E2E` classifies successful `Windows Release Gate` workflow-run completions before allocating the self-hosted Windows test job.
- An exact immutable updater release for the upstream source explicitly authorizes the expensive installed-client E2E.
- If no exact release exists, a clean skip is allowed only when current canonical `main` is proven by GitHub compare state to be ahead of that exact source, which identifies the intentional stale-release path.
- If the missing-release source is still canonical `main`, or the source/main relationship is divergent/ambiguous, classification fails closed rather than masking a publication defect.
- The existing in-job exact-release resolution remains in place as defense in depth.
- `Get-UpdaterInstalledClientE2EDecision` centralizes the state machine and `Test-UpdaterReleasePolicy.ps1` covers published, superseded, canonical-missing, divergent, permissions, ancestry, and heavy-job gating invariants.

## Verification boundary

This patch changes release/CI policy and therefore changes what green means. Require fresh exact-head Workflow Feature, Updater Publication, Heaven Toolbox Ownership, and MHW Product Security gates on the final reconciled v8.8.74 candidate before integration. Earlier green evidence does not authorize this changed workflow/test/version boundary.

After integration, verify remote `main` contains the classifier job, shared decision helper, regression assertions, and synchronized 8.8.74 metadata. A successful superseded upstream release-gate completion should produce a clean classifier skip, while an exact published source must still run the real installed-client E2E.

## Unresolved risks and coordination

PRs #582, #585, and #586 were opened against the v8.8.73 era and may contain stale version/continuity surfaces. After v8.8.74 becomes canonical, each active owner must reconcile against fresh `main`, preserve unique work, use a later patch identity as required, and rerun exact-head gates before integration.

Issue #578, #558, #559, RECOVERY-005, RECOVERY-007, external production trust-anchor provisioning for #350, and account-tier ruleset/certificate constraints for #354 remain independent work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release supply-chain rules. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
