# v8.8.81 allowlisted updater E2E evidence — canonical state

v8.8.80 source `e7af34331b608fca3d115d2dfe20f5c8e69a5973` remains the last closed hosted-Windows verification boundary. v8.8.81 hardens the installed-client updater E2E persistence trust boundary under issue #632.

## Behavior

- Raw runtime E2E diagnostics remain transient; canonical Git history receives only the versioned `mhw-mod-manager/updater-installed-client-e2e-durable/v1` projection.
- Durable target-source identity must match the exact workflow-tested SHA.
- Release identity, update/rollback terminal state, installed UI acceptance, and the three preservation sentinel hashes are validated before persistence.
- Absolute runner paths/usernames, process and attempt IDs, target-only path inventories, product logs, timestamps, runner environment metadata, and unknown future properties are excluded by construction.
- The compatible `evidence_sha256=` header preserves provenance for the transient raw evidence without embedding its contents.

## Verification boundary

Current hosted-Windows closure: v8.8.81 source `1d21a7ee99774c6820e16421837e9451ef2e3ef6` passed run `37098443475` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.81-heaven-windows-closure.log`.

The tested source remains `1d21a7ee99774c6820e16421837e9451ef2e3ef6` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Stale #602/#603 ownership expired without a heartbeat; their product work remains separate and must be reconciled against the post-v8.8.81 main if continued.

Every successor must preserve the permanent continuity constitution, active Learned Rules, exact-input verification, and the durable-evidence privacy boundary, and recursively propagate the same obligation.
