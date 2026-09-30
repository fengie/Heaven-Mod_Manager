# Repository context router

Do not use this file as a historical status snapshot. Current identity and verification scope live in `_AGENT_CONTEXT/CURRENT_REVISION.json`; current work lives in `NEXT-AGENT-START-HERE.md`, `CURRENT_STATE.md`, and `NEXT_STEPS.md`.

## Progressive read order

After the compact core from `AGENTS.md`:

1. Read `_AGENT_CONTEXT/CURRENT_REVISION.json` for exact current version/status and verification applicability.
2. Read `NEXT-AGENT-START-HERE.md` when continuing active repository work.
3. Retrieve only task-relevant sections from:
   - `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` for project Core Rules and domain invariants;
   - `_AGENT_CONTEXT/BUG_PRECEDENTS.md` and `LEARNED_RULES.md` for relevant prior failure classes;
   - `CURRENT_STATE.md` / `NEXT_STEPS.md` for live project state;
   - `ARCHITECTURE.md`, `VERIFICATION.md`, `DECISIONS.md`, `KNOWN_ISSUES.md`, and specialized evidence/audits when the task touches those domains.
4. Inspect relevant source/tests directly before editing.

Use the hash-verified context search/heading/pagination path advertised by Agent Control. Large ledgers and historical audits are indexed reference material, not mandatory end-to-end startup reading.

## Current-state discipline

Current files contain only current decisions, active risks, and next actions. Closed revision-by-revision narratives belong in Git history or dedicated evidence/history documents, not stacked at the top of active context.

Historical verification proves only its exact source/artifact. Never promote an old green run to changed source.

## Durable handoff

When truth changes, update the smallest authoritative current file. Do not copy the same status into several documents. A handoff should state exact revision, changed behavior, checks actually run, unresolved risks, ownership/integration state, and ordered next actions.
