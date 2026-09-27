# Agent continuity protocol — mandatory

This project is intentionally self-handing-off. Completing code changes is only part of the task. Every agent who modifies this project must preserve the knowledge needed by the next agent **inside the source handoff archive itself**.

## Non-negotiable rule

Before delivering a new source zip, update `_AGENT_CONTEXT/` with everything materially learned during the task and preserve this file for the next agent.

**Do not break the chain.**

The next agent must be able to continue safely without access to prior chat history.

## What must be preserved

Record, when applicable:

- current version and exact parent/baseline version;
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

Do not turn these files into vague summaries. Prefer concrete class/file names, invariants, commands, version numbers, and evidence.

## Required update discipline

1. Read `NEXT-AGENT-START-HERE.md` and `_AGENT_CONTEXT/README_FIRST.md` before editing.
2. Update context files **while working**, not only after the work is complete.
3. Clearly distinguish:
   - confirmed by build/test/runtime evidence;
   - confirmed by static/source inspection;
   - hypothesis / recommended future work.
4. Never mark verification state known-good merely because code looks correct.
5. Before packaging, run `scripts/Test-AgentHandoff.ps1`.
6. For a source handoff, prefer `Build Source Handoff.bat` / `scripts/Build-Source-Handoff.ps1` so context and verification evidence cannot be accidentally omitted.
7. Preserve this continuity protocol and explicitly instruct the next agent to follow it again.

## Applies beyond coding

If a future task produces a different durable artifact (research package, dataset, spreadsheet, document set, configuration bundle, etc.), adapt the same rule: package the important learned context, assumptions, provenance, decisions, verification status, and next steps with the deliverable, and tell the next agent to continue the practice.

## Privacy / secrets

Do not copy passwords, API keys, tokens, private credentials, or unnecessary personal data into handoff files. Preserve only project-relevant technical context.
