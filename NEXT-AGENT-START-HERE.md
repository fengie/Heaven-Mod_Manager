# v8.8.91 catalog + updater storage lifecycle — handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #669 plus the already-integrated #730 tranche

## v8.8.91 behavior

- Preserve #730's visible bulk-scope UX and consolidated residual catalog/migration/rules fixes already on main.
- Use manager-owned catalog-download leases and reclaim only proven exited-owner residue within age/byte-quota bounds.
- Keep active, ambiguous, unknown-shape, unleased, and reparse-point state fail-closed.
- Require PID + process-start identity on new prepared updater leases; legacy missing-start leases are reclaimable only after the PID is proven gone.
- Preserve the v8.8.89 privacy/legal/accessibility controls and all existing updater provenance, exact SDK, filesystem containment, and durable-evidence boundaries.

## Verification boundary

Current hosted-Windows closure: v8.8.91 source `936a265994610791f647f5607f162f5539e8a320` passed run `37157439747` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.91-heaven-windows-closure.log`.

The tested source remains `936a265994610791f647f5607f162f5539e8a320` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk

v8.8.91 does not yet have closed canonical Windows verification for its exact integrated source. Do not claim the release closed until that post-integration verification passes and its durable evidence is persisted.

## Next action

Integrate the exact green v8.8.91 candidate, read back remote `main`, then run fresh canonical Windows verification for the exact integrated source and persist closure evidence. After that, resume the highest-priority actionable unowned project-plan item.

## Execution/offload note

When a callable Heaven/Agent Control execution surface is unavailable in the current session, use repository CI/GitHub evidence and do not fabricate local execution.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. As the successor, you must preserve exact-input verification, catalog/updater ownership and cleanup invariants, updater publication/parity/provenance, exact SDK policy, filesystem containment, and durable-evidence privacy, and must propagate the same obligation to the next successor.

**Do not break the chain.**
