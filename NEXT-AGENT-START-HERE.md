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

Last closed canonical Windows verification remains v8.8.89 source `b13f2210211973d23ff0e6ca2f449dade87919f5`, run `37150713097`.

v8.8.91 requires fresh exact integrated-source Windows verification after integration. Do not call v8.8.91 released/closed until that run passes and truthful evidence is persisted.

## Next action

Integrate the exact green v8.8.91 candidate, read back remote `main`, then run fresh canonical Windows verification for the exact integrated source and persist closure evidence. After that, resume the highest-priority actionable unowned project-plan item.

## Execution/offload note

When a callable Heaven/Agent Control execution surface is unavailable in the current session, use repository CI/GitHub evidence and do not fabricate local execution.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, catalog/updater ownership and cleanup invariants, updater publication/parity/provenance, exact SDK policy, filesystem containment, durable-evidence privacy, and propagate the same obligation to the next successor.

**Do not break the chain.**
