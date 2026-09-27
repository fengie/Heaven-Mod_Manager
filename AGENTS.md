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
