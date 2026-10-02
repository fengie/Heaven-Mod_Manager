# v8.8.70 stale-profile rediscovery — candidate state

v8.8.69 is integrated on canonical main as `7ee321a349098849b9e4db302b20dfa1595ca13b`. Its exact PR #565 head `fdecfcccf9e04850acb983e91582d3c915283efa` passed Workflow Feature `36981898989`, MHW Product Security `36981899021`, and Heaven Toolbox Ownership `36981898930`.

Issue #571 / PR #572 is the current v8.8.70 lane.

## Candidate behavior

- Automatic discovery no longer lets a structurally valid but executable-missing same-root profile suppress rediscovery forever.
- A live same-root profile remains a no-op.
- A stale same-root profile may be repaired while preserving ID and active selection.
- Missing store/Steam metadata may be filled from discovery.
- MHW identity from the existing profile or Steam app 582010 requires `MonsterHunterWorld.exe`; a launcher/helper executable is rejected.
- The repair executable remains constrained to the discovered root through `GameProfile.NormalizeRelative` containment.
- Deterministic tests cover inferred executable repair and both MHW guard forms.

## Verification boundary

No v8.8.70 merge authorization is claimed yet. All three required exact-head PR gates must pass after the final metadata commit.

RECOVERY-007 still requires representative Windows/runtime discovery proof after source integration. #558 and #559 remain separate open work.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate the continuity obligation onward.
