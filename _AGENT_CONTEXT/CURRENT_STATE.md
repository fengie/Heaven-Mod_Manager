# v8.8.77 updater E2E runner + continuity-state repair — canonical state

v8.8.77 keeps the bounded Vortex handoff boundary from v8.8.76, repairs updater installed-client E2E classifier allocation, and closes the recurring gap between persisted hosted-Windows evidence and the canonical successor state.

## Behavior

- The updater publication classifier runs on the known-good self-hosted Heaven Windows runner while retaining read-only permissions, exact immutable-release classification, canonical-main ancestry checks, and fail-closed heavy E2E gating.
- Hosted-Windows closure persistence now treats evidence plus continuity as one logical transition: exact source/run evidence, `CURRENT_REVISION.json`, this state surface, and the successor handoff advance together before the bot commit is pushed.
- Tested-source identity remains distinct from the later evidence-only commit created to persist proof.
- The continuity validator binds any current-version closure log to its source SHA and run ID and rejects stale candidate/integration prose that contradicts already-persisted proof.
- The v8.8.76 Vortex interoperability contract remains credential-free, schema-bounded, preview-first, path/hash validated, isolated from live deployment, and atomic on export.
- Steam Workshop remains unsupported for MHW until a reviewed operation-specific contract exists.

## Verification boundary

Current hosted-Windows closure: v8.8.77 source `cd90cff3cec585b6c09e2bb68992247753f6efb9` passed run `37072838484` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.77-heaven-windows-closure.log`.

The tested source remains `cd90cff3cec585b6c09e2bb68992247753f6efb9` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

- #350: production updater signing trust anchor/private-key provisioning and real signed-release E2E.
- #354: repository-admin/ruleset protection and stable Windows publisher identity.
- #281: only the separate Steam Workshop applicability tranche remains after Vortex handoff integration.
- #558/#559 plus representative RECOVERY-005/RECOVERY-007 acceptance remain independent queues.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve exact-input verification and the credential-free Vortex boundary, and recursively propagate the continuity obligation.
