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

The last closed hosted-Windows boundary is v8.8.76 source `afc3ec4f0be8ba36a93b2b880edc6a3cd9de0f52`, run `37060606949` (26/26 PASS), with exact evidence at `_AGENT_CONTEXT/EVIDENCE/v8.8.76-heaven-windows-closure.log` persisted by evidence-only commit `66cbd9cce0df375b114f907afd7a9c1a93ea4742`.

v8.8.77 changes updater E2E runner routing and verification/continuity infrastructure after that source, so it requires fresh exact-source verification. Once the v8.8.77 closure is produced, the release workflow must synchronize this section and the machine-readable continuity projection before committing the evidence.

## Remaining independent work

- #350: production updater signing trust anchor/private-key provisioning and real signed-release E2E.
- #354: repository-admin/ruleset protection and stable Windows publisher identity.
- #281: only the separate Steam Workshop applicability tranche remains after Vortex handoff integration.
- #558/#559 plus representative RECOVERY-005/RECOVERY-007 acceptance remain independent queues.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve exact-input verification and the credential-free Vortex boundary, and recursively propagate the continuity obligation.
