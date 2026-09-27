# Agent Instructions

This repository is the canonical working state for MHW Manual Mod Manager.

Before modifying code or durable project state:

1. Verify the actual canonical `main` HEAD; repository state overrides stale chat/prompt state.
2. Inspect `git status` and preserve unexplained local work.
3. Inspect recent relevant Git history/diffs.
4. Read `NEXT-AGENT-START-HERE.md`.
5. Read `_AGENT_CONTEXT/CURRENT_REVISION.json`.
6. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`.
7. Read active rules in `_AGENT_CONTEXT/LEARNED_RULES.md`.
8. Follow the remaining read order in `_AGENT_CONTEXT/README_FIRST.md`.
9. Preserve documented verification, transaction, filesystem, recovery, safety, and UI/lifetime invariants unless an explicitly scoped task justifies changing them.
10. Keep one independently verifiable architecture/source boundary at a time.
11. Update durable handoff/context as you work, not only at the end.
12. Commit and push meaningful checkpoints; do not leave expensive discoveries only in chat or local state.
13. Run the handoff/verification checks appropriate to the change and report verification only for exact inputs actually checked.
14. Explicitly pass this continuity obligation to your successor, and require that successor to pass it to the agent after them.

GitHub `fengie/mhw-mods` on `main` is the source of truth. Source ZIPs are optional export/release artifacts, not canonical development state.

The next agent must be able to continue without previous chat history.

Every agent must preserve this rule system and require its successor to preserve and recursively propagate it again. Core continuity rules may be weakened only with explicit user authorization.

If context, execution time, tool access, or usage allowance becomes dangerously low, stop expanding scope and enter the preservation mode defined in the continuity protocol.

**Do not break the chain.**
