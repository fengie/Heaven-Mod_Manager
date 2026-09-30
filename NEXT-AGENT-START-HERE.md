# v8.8.48 agent-training refactor — current handoff

This file is a current handoff, not a revision diary. Older handoffs remain available in Git history and evidence files.

## Current assignment

Refactor the active programming-agent training/governance system for lower context cost and better execution behavior. The candidate branch is `refactor/agent-training-v8.8.47-20260930`, based on canonical `main` `7def479cd4c327b0ed45790a5b28a6b68c5d3c67`.

The intended result is a smaller core with task-relevant progressive retrieval, one universal operating standard, role templates that contain deltas instead of copied common contracts, concise current-state/handoff files, semantic continuity validation, and enforced context budgets.

## What must remain true

- `fengie/mhw-mods/main` remains canonical and the cross-repository training baseline.
- MHW-specific safety, release, filesystem, transaction, updater, plugin, process-ownership, dependency, and machine invariants remain available in indexed repository policy/continuity.
- Exact-source verification, regression coverage for behavior changes, collision-safe ownership, remote-main delivery, visible patch/version progress, and durable successor handoff remain explicit.
- `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` remains the permanent continuity constitution; Core Rules are not weakened without explicit user authorization.
- Large history/incident ledgers remain searchable/indexed rather than injected into every startup.

## Acceptance for this candidate

Run the repository's handoff validator and negative fixtures, Agent Control repository-bootstrap tests, prompt/context byte-budget checks, and any repository gate triggered by these workflow/tooling edits. Verify the compact core and manager prompt stay under their enforced budgets.

Do not claim product/runtime verification that was not rerun for this candidate. This change touches governance/prompt/bootstrap/test tooling, not MHW product behavior.

## Existing project state outside this refactor

Agent Manager P0 work remains active until authenticated dispatch/observe/recover/stop behavior is proven end to end under the existing acceptance criteria. Existing unrelated product/branch work—including the open Mods-width UI fix—must stay separate and must not be absorbed into this governance branch.

## Successor handoff

Before closing this candidate, record the final exact SHA, checks/results, integration status, any remaining duplicated instruction owners or validator brittleness, and the next concrete improvement opportunity. A fresh successor must be able to continue without private chat history.
