# v8.8.76 Vortex handoff interoperability — canonical state

v8.8.75 source `5d864d4d5189e5ad7c0ec535886c506fcc07c513` is the verified predecessor boundary. v8.8.76 adds bounded Vortex handoff interoperability without making Vortex a catalog backend or credential/state dependency.

## Behavior

- Only the explicit v1 MHW handoff schema is accepted.
- Exported state is allowlisted to package/profile identity, enable/priority intent, optional Nexus IDs, and optional managed-file SHA-256 values.
- Generic source URLs and unknown fields are not part of the contract, preventing credential-bearing/signed URLs from entering the handoff.
- Game identity and managed paths are normalized and validated; malformed, wrong-game, traversal, ambiguous, missing, and hash-mismatched entries fail closed.
- Import re-reads and re-matches immediately before saving, creates only an isolated manager profile, and never mutates live deployment.
- Export uses same-directory temporary files and atomic replacement to prevent partial writes from replacing a valid handoff.
- Steam Workshop remains unsupported for MHW until a reviewed operation-specific contract exists.

## Verification boundary

Predecessor evidence: hosted Windows verification run `37015488962` passed 26/26 for v8.8.75 source `5d864d4d5189e5ad7c0ec535886c506fcc07c513`, with evidence persisted on canonical main at `43252f835351f40083cf8823d927cf2e24b1a656`.

Fresh exact-head gates are required for the final v8.8.76 candidate because interoperability code, tests, XAML, schema, continuity validation, and release metadata changed. Persist the exact candidate/run evidence after integration; do not inherit predecessor green status.

## Remaining independent work

- #350: production updater signing trust anchor/private-key provisioning and real signed-release E2E.
- #354: repository-admin/ruleset protection and stable Windows publisher identity.
- #281: only the separate Steam Workshop applicability tranche remains after Vortex handoff integration.
- #558/#559 plus representative RECOVERY-005/RECOVERY-007 acceptance remain independent queues.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve the credential-free Vortex boundary, and recursively propagate the continuity obligation.
