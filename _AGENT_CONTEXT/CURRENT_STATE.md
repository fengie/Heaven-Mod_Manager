# v8.8.78 authoritative MHW discovery hardening — canonical state

v8.8.76 source `afc3ec4f0be8ba36a93b2b880edc6a3cd9de0f52` is the last closed verification boundary, with hosted Windows run `37060606949` passing 26/26 checks. v8.8.78 preserves the concurrent v8.8.77 updater-runner repair, closes two authoritative MHW discovery gaps, and repairs stale verification/release metadata.

## Behavior

- A live generic profile at the authoritative MHW root is rebuilt from `GameProfile.MonsterHunterWorld` when discovery resolves the real `MonsterHunterWorld.exe`.
- User-owned ID, display name, save path, and explicit store choice survive canonical reconstruction.
- New Steam app 582010 discovery fails closed when the resolved executable is not `MonsterHunterWorld.exe`.
- Same-root duplicate ownership, active-marker repointing, non-MHW live-profile behavior, and restart idempotency remain preserved.

## Verification boundary

Closed predecessor evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.76-heaven-windows-closure.log`, source `afc3ec4f0be8ba36a93b2b880edc6a3cd9de0f52`, hosted Windows run `37060606949`, 26/26 PASS.

Fresh exact-input verification is required for the final v8.8.78 tree.

## Remaining independent work

- RECOVERY-007 still needs representative installed Windows/runtime discovery proof.
- #350/#354 retain external prerequisites; #281 retains Steam Workshop applicability; #558/#559 and RECOVERY-005 remain independent.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve the fail-closed discovery boundary, and recursively propagate the continuity obligation.
