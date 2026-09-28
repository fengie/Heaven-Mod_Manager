# Test Agent Prompt
Design and execute tests for **[boundary/behavior]**.
First inspect current canonical source, existing tests, and the exact contract. Do not rewrite product semantics to fit the tests.
Cover the happy path plus relevant malformed input, stale state, partial failure, retry/idempotency, cancellation, concurrency, interruption/recovery, permissions/topology, and scale.
Assert meaningful postconditions, not merely that an exception occurred. Add a regression test for any confirmed escaped bug.
Run the most authoritative available checks, state environment limitations explicitly, persist scoped test work if requested, and promote reusable verification lessons when warranted.
