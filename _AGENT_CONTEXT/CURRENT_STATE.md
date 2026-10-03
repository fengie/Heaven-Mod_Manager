# v8.8.91 catalog + updater storage lifecycle — canonical state

v8.8.91 carries forward the v8.8.90 byte-stability/CI baseline, includes the already-integrated #730 visible-scope and residual catalog/migration/rules regressions, and adds #669 ownership-safe cleanup for catalog acquisition scratch and prepared updater leases.

## Behavior

- Catalog acquisition scratch uses manager-owned lease records so abandoned downloads can be reclaimed without guessing ownership.
- Cleanup reclaims only proven exited-owner catalog archives and temporary lease residue under age and byte-quota bounds; active, ambiguous, unknown-shape, unleased, and reparse-point state remains fail-closed.
- Prepared updater transaction leases require certified process ID plus process-start identity for new writes.
- Legacy updater leases that lack process-start identity become reclaimable only after their PID is proven gone; live or reused PIDs remain preserved.
- The v8.8.91 release also includes #730's visible bulk-scope UX and consolidated catalog/migration/rules regression fixes already present on canonical main before this patch.
- The v8.8.89 privacy/legal/accessibility baseline and all updater provenance, filesystem containment, exact-SDK, and durable-evidence safeguards remain in force.

## Verification boundary

The last closed hosted-Windows source remains v8.8.89 `b13f2210211973d23ff0e6ca2f449dade87919f5`, run `37150713097`, with evidence at `_AGENT_CONTEXT/EVIDENCE/v8.8.89-heaven-windows-closure.log`.

v8.8.91 has no closed canonical Windows evidence yet. After integration, run fresh exact integrated-source Windows verification and persist the v8.8.91 closure evidence before calling the patch released/closed. Evidence-only persistence commits do not change tested-source identity.

## Remaining independent work

Keep #559/#558 catalog discovery/browse scale, #350/#354 external trust/admin prerequisites, #709 reproducible NuGet restore, and remaining recovery/installed-Windows acceptance independent unless current live state proves them resolved.

Every successor must preserve the permanent continuity constitution, exact-input verification, catalog/updater ownership and cleanup invariants, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and recursively propagate the same obligation.
