# v8.8.70 stale installed-game profile repair — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `fix/issue571-stale-profile-repair-v8.8.70-20261002-chatgpt`
Issue: #571
Parent canonical main: `7ee321a349098849b9e4db302b20dfa1595ca13b`

## Completed predecessor boundary

v8.8.69 PR #565 exact head `fdecfcccf9e04850acb983e91582d3c915283efa` passed Workflow Feature `36981898989`, MHW Product Security `36981899021`, and Heaven Toolbox Ownership `36981898930`, then merged as `7ee321a349098849b9e4db302b20dfa1595ca13b`. Preserve its capability-gated provider search and 1000-row storage/UI capacity agreement.

## v8.8.70 candidate

- If discovery sees the same game root as a persisted profile whose executable is missing, repair that profile instead of suppressing rediscovery.
- Preserve the existing profile ID and active-game selection.
- Fill missing Store and SteamAppId values from the discovered candidate.
- Leave same-root profiles with a live executable untouched.
- Refuse to repair a Monster Hunter: World profile through any executable other than `MonsterHunterWorld.exe`.
- Deterministic integration tests cover stale repair, live-profile no-op behavior, active-game preservation, and the MHW executable guard.

This candidate addresses issue #571 but does **not** close RECOVERY-007 by itself; representative Windows/runtime installed-game discovery proof remains required.

## Verification state

No v8.8.70 green claim exists yet. The source/test checkpoint from draft PR #572 was replayed onto fresh v8.8.69 main and release/continuity metadata was advanced to v8.8.70. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final v8.8.70 head before merge.

## Unresolved risks

- **Unresolved risk:** v8.8.70 is not merge-authorized until all three required exact-head PR gates pass.
- **Unresolved risk:** RECOVERY-007 still requires representative Windows/runtime discovery proof after this stale-profile lifecycle repair.
- **Unresolved risk:** #558 remains open for broader provider-aware pagination/browse scaling and #559 remains open for Browse Mods discovery UX.
- Existing external signing/ruleset blockers and preserved issue #281 recovery-branch provenance remain unchanged.

## Ordered continuation

1. Open the v8.8.70 replacement PR for #571 from this fresh-main branch.
2. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on the exact final head.
3. Repair only concrete failures and preserve the completed v8.8.69 catalog boundary.
4. Refresh canonical `main`, issue #571, and PR mergeability immediately before integration.
5. Merge only the exact green head and verify the intended tree on remote `main`.
6. Close the old draft PR #572 as superseded only after its unique two-file semantics are integrated.
7. Close issue #571 after integration evidence is confirmed; keep RECOVERY-007 active for runtime proof.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
