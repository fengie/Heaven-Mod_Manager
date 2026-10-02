# v8.8.70 stale-profile rediscovery — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `fix/issue571-stale-profile-repair-20261002`
Issue: #571
Parent canonical main: `7ee321a349098849b9e4db302b20dfa1595ca13b`

## Completed predecessor boundary

v8.8.69 PR #565 exact head `fdecfcccf9e04850acb983e91582d3c915283efa` passed Workflow Feature `36981898989`, MHW Product Security `36981899021`, and Heaven Toolbox Ownership `36981898930`, then squash-merged to main as `7ee321a349098849b9e4db302b20dfa1595ca13b`. The integrated result keeps per-keystroke Browse Mods filtering local, capability-gates explicit provider search, persists remote rows through the source-aware cache, isolates provider failures, and raises the repository search ceiling to 1000. Issue #558 remains open for broader pagination/scale work.

## v8.8.70 candidate

- Same-root profiles with a live executable remain untouched.
- Same-root profiles with a missing executable may be repaired from a valid discovered executable.
- Repair preserves the profile ID and does not change the active-game marker.
- Missing Store / SteamAppId metadata is filled from discovery without overwriting non-empty persisted values.
- Executables outside the discovered root fail through the existing relative-path containment validation rather than being rebound silently.
- MHW repair refuses any non-`MonsterHunterWorld.exe` executable when MHW identity is known from the persisted adapter, persisted Steam app id, or discovered Steam app `582010`.
- Deterministic tests cover inferred executable discovery, live-profile no-op, persisted-MHW protection, and the stale-generic + MHW-discovery identity edge case.

## Verification state

v8.8.70 is not merge-authorized yet. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final PR #572 head after this metadata commit.

## Unresolved risks

- #571 remains open until the exact v8.8.70 head is green and merged.
- RECOVERY-007 still needs representative installed Windows/runtime discovery proof before the broader recovery item can be marked DONE.
- #558 remains open for broader provider-aware catalog pagination/discovery and scale/performance work.
- #559 remains open for Browse Mods filtering, sorting, provider health, and richer discovery states.
- Existing external signing/ruleset blockers, artifact-storage constraints, and preserved recovery-branch provenance remain unchanged.
- Local Codex on `heaven` remains usage-limit blocked until 2026-10-07; exact GitHub/Heaven CI is the verification authority for this candidate.

## Ordered continuation

1. Mark PR #572 ready and run Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final head.
2. Repair only concrete failures; preserve the v8.8.69 catalog boundary and all stale-profile invariants above.
3. Refresh `main`, issue #571 ownership, and PR mergeability immediately before integration.
4. Squash-merge only the exact green head; verify canonical main and issue closure.
5. Persist a docs-only closure handoff with exact v8.8.70 integration evidence, then continue remaining #558/#559/RECOVERY-007 work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
