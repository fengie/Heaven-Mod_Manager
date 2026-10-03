# v8.8.79 automation state integrity — canonical state

v8.8.78 source `e6e6a91b40ac281f38d0f11dc6b7a77052ca4071` remains the last closed hosted-Windows verification boundary. v8.8.79 fixes two automation state-integrity defects under #619/#620.

## Behavior

- Indexed snapshot reparse entries are unlinked without recursive traversal; their DB record is deleted only after successful unlink so failures remain retryable.
- Last-known-good change detection is symmetric across saved/current mod IDs and reports removed mods.
- Case-insensitive mod identity and deterministic result ordering are preserved.

## Verification boundary

Current hosted-Windows closure: v8.8.79 source `6a10008fdcaae5112653593d62b41f5ef4668b36` passed run `37085548752` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.79-heaven-windows-closure.log`.

The tested source remains `6a10008fdcaae5112653593d62b41f5ef4668b36` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

- #559/#558 retain Browse Mods UX and scale work.
- #350/#354 retain external prerequisites.
- RECOVERY-005/RECOVERY-007 representative installed Windows acceptance remains independent.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve filesystem and exact-input verification boundaries, and recursively propagate the continuity obligation.
