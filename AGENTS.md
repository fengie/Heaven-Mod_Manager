# Agent Instructions

This repository is the canonical working state for MHW Manual Mod Manager.

Before modifying anything:

1. Read `NEXT-AGENT-START-HERE.md`.
2. Read `_AGENT_CONTEXT/CURRENT_REVISION.json`.
3. Follow `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`.
4. Read the remaining context in the order specified by `_AGENT_CONTEXT/README_FIRST.md`.
5. Inspect recent Git history/diffs relevant to the task before assuming older notes describe the current tree.
6. Never manually promote verification booleans or reuse cached evidence across changed fingerprints.
7. Preserve confirmed transactional, rollback, CAS, path-safety, and verification invariants unless the task explicitly requires a justified redesign.
8. Update `_AGENT_CONTEXT/` with material discoveries, decisions, evidence, regressions, uncertainties, changed files, and next steps as part of the same repository change.
9. Run the handoff and verification checks appropriate to the change before declaring work complete.

GitHub `fengie/mhw-mods` on `main` is the source of truth. Source ZIPs are optional export/release artifacts, not the canonical development state.

The next agent must be able to continue without access to previous chat history.

**Do not break the chain.**

## Checkpoint cadence

The user explicitly requests frequent durable checkpoints before session credits run out.
Push the working feature branch at each meaningful checkpoint (for example: a coherent
backend change, integrated UI, or validation milestone), even if the overall task is
still in progress. Pair every checkpoint with updated `_AGENT_CONTEXT/` notes stating:
current plan, completed changes, exact test evidence, known failures/limits, and the
next concrete action. Clearly label unverified work; never promote verification
booleans to make a checkpoint look finished. Do not leave the only copy of substantial
work in a transient workspace. Preserve the user's existing authorization; checkpoint
pushes do not require another confirmation. Do not merge the PR without authorization.

## Knowledge transfer

The user explicitly requests comprehensive project knowledge in Git and propagation
to every successor. Read `_AGENT_CONTEXT/SESSION_HANDOFF_2026-09-27.md` and the latest
`CHECKPOINT.md`. Update relevant architecture, decisions, evidence, limits and next
steps as you learn; keep current status free of stale contradictions. Push the handoff
with each meaningful checkpoint. Remind the next agent to repeat this obligation.
Record task-relevant knowledge and user instructions, not credentials or unrelated
personal information. **Do not break the chain.**
