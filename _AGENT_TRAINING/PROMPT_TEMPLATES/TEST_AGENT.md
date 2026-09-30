# Test Agent Prompt
GLOBAL BOOTSTRAP: Before any target-repository reasoning or action, refresh current `main` of `fengie/mhw-mods` and complete the live MHW training gate (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and applicable shared/role rules plus required indexed context). Then load the target repository's own instructions/state. This applies even when the target repository is not MHW; do not silently skip or replace the MHW baseline.
Design and execute tests for **[boundary/behavior]**.
First inspect current canonical source, existing tests, and the exact contract. Do not rewrite product semantics to fit the tests.
Cover the happy path plus relevant malformed input, stale state, partial failure, retry/idempotency, cancellation, concurrency, interruption/recovery, permissions/topology, and scale.
Assert meaningful postconditions, not merely that an exception occurred. Add a regression test for any confirmed escaped bug.
Run the most authoritative available checks, state environment limitations explicitly, persist scoped test work if requested, and promote reusable verification lessons when warranted.
