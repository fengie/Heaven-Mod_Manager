# v8.8.82 updater helper request topology — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issues #635 and #647

## v8.8.82 behavior

- Bind each updater helper transaction identity to the target manifest plus canonical install, staging, and manager-home roots.
- Validate the complete helper request topology before mutex acquisition or recovery-state access.
- Require request, backup, journal, health, pending, and staging paths to match the canonical updater layout; reject cross-attempt/root substitution and noncanonical path aliases.
- Reject malformed health/process identity, updater-owned health arguments, and existing reparse-point substitution.
- Require canonical pending-update state to agree with the request manifest and staging root while preserving already-confirmed terminal no-op behavior after pending cleanup.
- Stop retaining the verified release ZIP once extraction and product verification are complete.
- Retire confirmed staging immediately and clean sufficiently old orphan staging / terminal confirmed-or-rolled-back transaction directories without touching pending or recovery-required state.
- Fail closed on malformed pending state and refuse reparse-point traversal during cleanup.
- Preserve the v8.8.81 durable installed-client E2E privacy boundary unchanged.

## Verification boundary

Current hosted-Windows closure: v8.8.82 source `f46793a18700524ac7b5fb34770200206d6595f2` passed run `37107057966` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.82-heaven-windows-closure.log`.

The tested source remains `f46793a18700524ac7b5fb34770200206d6595f2` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and next work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Any stale branch that predates v8.8.82 must reconcile against current canonical main and allocate a later patch before integration.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, the updater request-topology boundary, the bounded/state-aware updater storage-retention boundary, and the durable-evidence privacy boundary. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
