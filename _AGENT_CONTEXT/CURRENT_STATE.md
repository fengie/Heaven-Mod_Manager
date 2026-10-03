# v8.8.79 automation state integrity — canonical state

v8.8.78 source `e6e6a91b40ac281f38d0f11dc6b7a77052ca4071` remains the last closed hosted-Windows verification boundary. v8.8.79 fixes two automation state-integrity defects under #619/#620.

## Behavior

- Indexed snapshot reparse entries are unlinked without recursive traversal; their DB record is deleted only after successful unlink so failures remain retryable.
- Last-known-good change detection is symmetric across saved/current mod IDs and reports removed mods.
- Case-insensitive mod identity and deterministic result ordering are preserved.

## Verification boundary

Fresh exact-input Windows verification is required for v8.8.79 because Automation production source, tests, and release metadata changed. Do not inherit v8.8.78 green status.

## Remaining independent work

- #559/#558 retain Browse Mods UX and scale work.
- #350/#354 retain external prerequisites.
- RECOVERY-005/RECOVERY-007 representative installed Windows acceptance remains independent.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve filesystem and exact-input verification boundaries, and recursively propagate the continuity obligation.
