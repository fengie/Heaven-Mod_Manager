# Programming-Agent Prompting Guide

Strong prompts reduce wasted work by making source of truth, scope, acceptance, and recovery explicit.

## Required elements
- **Objective:** one concrete outcome.
- **Canonical truth:** tell the agent to verify repository/runtime state rather than trust prompt hashes.
- **Scope:** files/systems or boundary the task may change.
- **Non-goals:** adjacent work that must remain untouched.
- **Inputs:** docs, branches, incidents, artifacts, logs, or constraints that matter.
- **Acceptance criteria:** observable conditions that define completion.
- **Verification:** exact tests/builds/manual/platform checks required.
- **Persistence:** commit/push/handoff expectations.
- **Recovery:** behavior if interrupted or a required environment is unavailable.
- **Knowledge update:** require project docs and reusable trainer lessons to be considered.

## Avoid
- vague “improve/fix everything” objectives;
- stale commit hashes treated as authority;
- huge mixed scopes;
- instructions to make tests green without preserving semantics;
- acceptance criteria based only on subjective confidence;
- branch assumptions without fetch/status/history checks;
- output-only requests that omit repository updates when durable work is required.
- cross-runtime file contracts that test only synthetic fixtures; when one tool writes text/JSON for another tool to parse, include the real producer encoding/serialization behavior (for example Windows PowerShell UTF-8 BOM output).
- generated/persisted artifact claims that stop at producer success; round-trip persisted data through its real consumer and parse/execute generated scripts with their target interpreter.

## Parallelize when
Tasks are independent, ownership boundaries are clear, support research can proceed without mutating the same files, and integration criteria are explicit.

## Do not parallelize when
Several agents would edit the same contract, task order matters, one finding determines the correct implementation, or the verification environment is the bottleneck rather than implementation.

## Adaptive prompts for continuing swarms
A long-running or multi-wave swarm must not blindly replay its launch prompt. Re-synthesize the prompt immediately before each new worker/replacement starts from current canonical state, completed work, active ownership, failures/blockers, durable partial artifacts, verification results, and recent prompt lineage. Explicitly tell the new worker not to repeat completed work and to change approach after a failed/blocked attempt. Treat this generated context as a launch snapshot only: repository/runtime truth discovered during the mandatory training gate remains authoritative.

Persist enough prompt provenance (generation/hash/wave or equivalent) to prove that retries and later waves actually received updated context instead of a static template.

## Prompt patterns
Implementation prompts should state one boundary, non-goals, invariants, tests, persistence, and canonical-truth rules.
Integration prompts should require remote branch discovery, per-branch disposition, protection of newer canonical continuity, combined verification, and a durable ledger.
Recovery prompts should forbid new features and require reconstruction, closure/rollback, and repaired handoff state.
Audit prompts should separate observation from recommendation and avoid implementation claims without code+verification.

## Stale-state protection sentence
> Do not trust hashes, branch names, task status, or architectural assumptions in this prompt over the current canonical repository and runtime evidence.

## Knowledge sentence
> Before finishing, ask whether this task revealed reusable engineering knowledge; if so, generalize and update the company trainer without polluting it with project-specific state.
