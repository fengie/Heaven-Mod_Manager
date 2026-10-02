# v8.8.71 audit reliability hardening — candidate state

v8.8.70 PR #572 is integrated on canonical main as `350752d315ba6db1d329f726d181bad173b41514` after all three required exact-head gates passed.

## Candidate behavior

- Save capture retries boundedly when the live source changes and verifies SHA-256 identity before publishing the snapshot copy.
- Release publication performs a fail-closed current-main check before any public/private release mutation.
- Canonical `CURRENT_REVISION.json` is post-integration state; candidate/task-branch ownership is mechanically rejected.
- Browse Mods explicitly distinguishes no selection from selected detail state and does not enable install without an exact file.

## Verification boundary

No v8.8.71 merge authorization is claimed yet. Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership must pass on the exact final head after synchronized version/continuity metadata.

#575-#577 are the delivery issues for this tranche. #559 remains open after its explicit selection-state subtask. #558, RECOVERY-005, and RECOVERY-007 remain separate unfinished work.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate the continuity obligation onward.
