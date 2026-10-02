# v8.8.70 stale installed-game profile repair — candidate state

Issue #571 is **ACTIVE**. RECOVERY-007 remains **ACTIVE**.

v8.8.69 PR #565 exact head `fdecfcccf9e04850acb983e91582d3c915283efa` passed all three required PR gates and merged as `7ee321a349098849b9e4db302b20dfa1595ca13b`. Preserve its Browse Mods provider-search and 1000-row capacity behavior.

## Candidate change

The source/test checkpoint from draft PR #572 was replayed onto fresh v8.8.69 main and advanced to v8.8.70.

- Same-root persisted profiles with a live executable remain untouched.
- Same-root persisted profiles with a missing executable are repaired from a valid discovery candidate.
- Repair preserves the existing profile ID and active-game selection.
- Missing Store / SteamAppId metadata is filled from discovery.
- Monster Hunter: World repair refuses non-`MonsterHunterWorld.exe` candidates.
- Integration tests cover stale repair, live no-op, active-game preservation, and the MHW guard.

## Verification boundary

No verification from the old draft #572 branch transfers to this fresh v8.8.70 candidate. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final head before merge.

## Remaining work

After this candidate integrates, issue #571 may close. RECOVERY-007 still needs representative Windows/runtime installed-game discovery proof before it is DONE. #558 and #559 remain separate catalog/discovery UX work.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate the continuity obligation to the next agent.
