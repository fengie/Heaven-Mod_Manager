# v8.8.88 public release provenance — canonical state

v8.8.88 adds an updater-readable provenance/index layer for immutable public releases without weakening existing publication, parity, exact-source, or SDK gates.

## Behavior

- Deterministic provenance records bind version, updater build/tag/channel, exact 40-character source SHA, publication time, and SHA-256/size computed from the actual release ZIP and manifest bytes.
- The public `release-index.json` is append-only and idempotent: exact retries no-op, conflicting immutable identities or duplicate source bindings fail closed, and concurrent writers use compare-and-swap with bounded retry.
- Provenance reconciliation runs only after public/private immutable parity. If rerun-local bytes differ from the immutable release, the workflow downloads the published assets and verifies GitHub digest/size metadata before generating the record.
- A same-version source with no exact immutable updater release remains a clean no-op; an already-published exact release can repair missing provenance without republishing immutable assets.
- Artifact and `Resolve-Path` results are materialized as arrays so single-artifact provenance generation remains valid under Windows PowerShell 5.1 strict mode.
- Current exact .NET SDK provisioning/roll-forward policy from canonical main remains intact.

## Verification boundary

Last closed canonical Windows source is v8.8.87 `4d94e6700d6d027449ef45eeab9c753d8ce56c26`, run `37142564667`.

The provenance implementation at pre-version head `c83e29054550d696701b34ba74bf66d5902dbbfe` passed Updater Publication, Product Security, and Workflow Feature gates after the Windows PowerShell scalar-path repair. The reconciled v8.8.88 candidate includes verified evidence-only main `0f8d5973951d67d78f21815ee82ee650fe368d6a` and changes release metadata/workflow inputs, so it requires fresh exact-final-head verification. After merge, canonical main requires a fresh strict Windows Release Gate and downstream installed-client updater E2E before v8.8.88 closure is claimed.

## Remaining independent work

Keep #669 storage lifecycle, #559/#558 catalog UX/scale, #350/#354 external trust/admin prerequisites, and remaining recovery/installed-Windows acceptance independent.

Every successor must preserve the permanent continuity constitution, exact-input verification, public/private updater parity, append-only provenance identity, exact SDK policy, durable-evidence privacy, and recursively propagate the same obligation.
