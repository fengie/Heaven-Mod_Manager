# v8.8.82 updater topology + bounded storage retention — canonical state

v8.8.81 source `b46f7324d860c66dd3572013dbba571dec319c87` remains the last closed hosted-Windows verification boundary. v8.8.82 hardens the updater helper handoff trust boundary under issue #635 and closes the unbounded updater staging/terminal retention defect under issue #647.

## Behavior

- Each updater helper transaction directory is bound to the target manifest plus the canonical install root, staging root, and manager-home root.
- The helper validates the complete request topology before acquiring the update mutex or reading request-selected recovery state.
- The request file, backup, journal, health, pending, and staging paths must match the canonical updater transaction layout; cross-attempt and foreign-root substitution fail closed.
- Noncanonical absolute-path spellings, malformed health/process identity, updater-owned health arguments, and existing reparse-point substitutions are rejected before helper state consumption.
- Canonical pending-update state must agree with the request manifest and staging root. A transaction that is already confirmed may remain a safe no-op after pending cleanup.
- A successfully extracted and verified staged update no longer retains its downloaded release ZIP alongside the complete extracted payload.
- Confirmation retires the entire staging attempt after the recovery journal is durable and pending state has been removed.
- Startup maintenance removes only sufficiently old orphan staging attempts and terminal Confirmed/RolledBack transactions; it preserves the current pending attempt and every nonterminal/recovery-required transaction.
- Malformed pending state fails closed for staging cleanup, unknown updater-owned directory shapes are preserved, and cleanup refuses reparse-point traversal.
- The v8.8.81 allowlisted durable installed-client E2E evidence boundary remains unchanged.

## Verification boundary

The last closed hosted-Windows verification remains v8.8.81 source `b46f7324d860c66dd3572013dbba571dec319c87`, run `37103097571`, with exact evidence at `_AGENT_CONTEXT/EVIDENCE/v8.8.81-heaven-windows-closure.log`.

v8.8.82 changes updater helper/client/runtime/storage source, application startup maintenance, adversarial integration tests, and synchronized release/continuity metadata. Fresh exact-input verification is therefore required; no earlier green result should be inherited onto the final v8.8.82 source.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Stale pre-v8.8.82 branches must reconcile against canonical main and take a later patch if their unique work is continued.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, the updater request-topology boundary, and the durable-evidence privacy boundary, and recursively propagate the same obligation.
