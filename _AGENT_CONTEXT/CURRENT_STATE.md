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

Current hosted-Windows closure: v8.8.88 source `029b105426ef875bcd302dac3fff9305c395637f` passed run `37144494783` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.88-heaven-windows-closure.log`.

The tested source remains `029b105426ef875bcd302dac3fff9305c395637f` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

Keep #669 storage lifecycle, #559/#558 catalog UX/scale, #350/#354 external trust/admin prerequisites, and remaining recovery/installed-Windows acceptance independent.

Every successor must preserve the permanent continuity constitution, exact-input verification, public/private updater parity, append-only provenance identity, exact SDK policy, durable-evidence privacy, and recursively propagate the same obligation.
