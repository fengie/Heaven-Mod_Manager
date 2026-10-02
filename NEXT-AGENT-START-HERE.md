# v8.8.73 release build provenance hardening — canonical-ready handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Integrated predecessor: v8.8.72 / PR #581
Change set: issue #583 build-provenance attestation

## v8.8.73 behavior

- The Windows release gate resolves the exact updater ZIP name/digest from `artifacts/update-manifest.json` and recomputes SHA-256 before provenance.
- A mismatched, missing, malformed, or path-bearing artifact identity fails closed before publication.
- Supported repositories generate SLSA build provenance through SHA-pinned `actions/attest` and verify the exact ZIP with `gh attestation verify --repo fengie/mhw-mods` before either updater feed mutates.
- Public repositories attest automatically. Private repositories require GitHub Enterprise Cloud plus `MHW_ENABLE_GITHUB_ATTESTATIONS=true`; unsupported private runs record an explicit skip instead of making a false provenance claim.
- The existing stale-main guard, public-first/canonical-second transaction, immutable parity verification, and non-cancellable publication boundary remain intact.
- `Test-UpdaterReleasePolicy.ps1` structurally guards the new permissions, pin, exact subject wiring, ordering, entitlement gate, digest check, and verification step.

## Verification boundary

v8.8.72 predecessor exact head `c9904b0ba85854ec332bd14778e5e5531a50d582` passed Workflow Feature `36986285577`, Updater Publication `36986285827`, Heaven Toolbox Ownership `36986285858`, and MHW Product Security `36986285763`.

Those results do **not** authorize v8.8.73. Require fresh exact-head gates for the final provenance branch before integration, then read back canonical `main`. If release publication runs on a private non-Enterprise repository, an explicit attestation skip is expected; do not describe that run as attested.

## Unresolved risks and coordination

PR #582 existed before this change with a v8.8.73 working-title assumption but its branch still carried v8.8.72 version metadata. After v8.8.73 becomes canonical, #582 must rebase/reconcile onto it and consume the next patch version rather than overwriting or duplicating this release identity.

Issue #578, #558, #559, RECOVERY-005, and RECOVERY-007 remain independent work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release supply-chain rules. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
