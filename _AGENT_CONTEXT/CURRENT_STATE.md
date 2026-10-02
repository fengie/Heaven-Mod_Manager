# v8.8.72 audit reliability hardening — candidate state

v8.8.71 PR #573 is integrated and exact-head verified; current main additionally contains #580's release-evidence artifact trimming.

## Candidate behavior

- Live saves are copied only after bounded stability checks and cryptographic source/copy identity verification.
- Failed or cancelled snapshot creation removes incomplete snapshot payloads and never writes a successful snapshot row.
- Release publication is authorized only after a final canonical-main SHA check immediately before the first mutation.
- Active canonical continuity state cannot name a task branch, candidate status/source, or active PR.
- Browse Mods explicitly separates no-selection from detail state and does not enable install until one exact provider file is selected.

## Verification boundary

No v8.8.72 merge authorization is claimed yet. Required exact-head PR gates must pass after this fresh-main reconciliation and synchronized release/continuity metadata.

Issues #575-#577 are the closure boundary for this tranche. #559 remains open after its explicit selection-state subtask. Issue #578 and the remaining #558/RECOVERY work remain separate.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate the continuity obligation onward.
