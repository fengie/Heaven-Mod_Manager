# Agent continuity protocol — mandatory

This project is intentionally self-handing-off. Completing code changes is only part of the task. Every agent who modifies this project must preserve the knowledge needed by the next agent **inside the canonical Git repository**.

## Canonical source of truth

GitHub repository `fengie/mhw-mods` on branch `main` is the canonical development state.

- Git commits/diffs are the authoritative change history.
- `_AGENT_CONTEXT/CURRENT_REVISION.json` records the current handoff status and the exact source commit to which verification evidence applies.
- Source ZIPs are optional reproducible export/release artifacts. They may contain useful manifests, but they do not supersede the repository.
- `_AGENT_CONTEXT/SOURCE_HANDOFF_MANIFEST.json` describes its packaged ZIP, not the live repository state.

## Non-negotiable rule

Before finishing repository work, update `_AGENT_CONTEXT/` with everything materially learned during the task, update the current-revision record when source or verification state changes, and commit those handoff changes with the code they describe.

**Do not break the chain.**

The next agent must be able to continue safely without access to prior chat history.

## What must be preserved

Record, when applicable:

- current version, original baseline, immediate parent/revision lineage, and relevant Git commit;
- architecture and dependency direction;
- ownership of major projects/classes/files;
- important execution and data flows;
- invariants that must remain true;
- design decisions and why they were made;
- approaches tried and rejected, including why;
- regressions found and their root causes;
- known bugs, edge cases, limitations and uncertainties;
- what was actually verified versus merely inspected;
- build/test commands and required environment/toolchain;
- verification/cache state and whether it is promoted or bootstrap-only;
- source files materially changed in the current task;
- follow-up work that should be done next;
- useful debugging techniques and diagnostic files;
- external research that materially affected implementation choices;
- user requirements that future implementation must preserve.

Do not turn these files into vague summaries. Prefer concrete class/file names, invariants, commands, version numbers, commit SHAs, and evidence.

## Required update discipline

1. Read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, and `_AGENT_CONTEXT/README_FIRST.md` before editing.
2. Inspect recent Git history/diffs relevant to the task.
3. Update context files **while working**, not only after the work is complete.
4. Clearly distinguish:
   - confirmed by build/test/runtime evidence;
   - confirmed by static/source inspection;
   - hypothesis / recommended future work.
5. Never mark verification state known-good merely because code looks correct.
6. Keep `CURRENT_REVISION.json` explicit about which source commit the verification evidence applies to. Documentation-only commits need not pretend they re-verified production code.
7. Before declaring work complete, run `scripts/Test-AgentHandoff.ps1`.
8. Commit code and its corresponding handoff/context updates together or in an immediately adjacent documented commit so the history remains interpretable.
9. If producing a source ZIP, prefer `Build Source Handoff.bat` / `scripts/Build-Source-Handoff.ps1` so context and verification evidence cannot be accidentally omitted.
10. Preserve this continuity protocol and explicitly instruct the next agent to follow it again.

## Applies beyond coding

If a future task produces a different durable artifact (research package, dataset, spreadsheet, document set, configuration bundle, etc.), adapt the same rule: preserve the important learned context, assumptions, provenance, decisions, verification status, and next steps in the canonical workspace, and tell the next agent to continue the practice.

## Privacy / secrets

Do not copy passwords, API keys, tokens, private credentials, or unnecessary personal data into handoff files. Preserve only project-relevant technical context.

## User-required checkpoint pushes (2026-09-27)

Push meaningful checkpoints to the active feature branch while work is ongoing. Update
`CHECKPOINT.md` with the plan, what changed, tests actually run, remaining risks, remote
commit/PR links, and the exact next step. This is an explicit user preference intended
to preserve progress before credits expire. Keep unverified checkpoints clearly marked.
Do not wait until the whole feature set is complete to make work durable.
