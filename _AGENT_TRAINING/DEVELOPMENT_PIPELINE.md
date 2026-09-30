# Generic Development Pipeline

Default flow:

**inspect → understand → choose → implement → test → verify → integrate → document → hand off**

The pipeline is adaptive. Use stages because the risk needs them, not because a checklist contains them.

## Baseline

For every implementation task:

1. **Inspect** canonical state, relevant code/tests/contracts, and ownership.
2. **Understand** the behavior, invariants, acceptance criteria, and failure boundary.
3. **Choose** the smallest coherent change and explicit non-goals.
4. **Implement** the root-cause fix or requested behavior without unrelated expansion.
5. **Test** the changed behavior with the narrowest useful check and regression coverage when behavior changed.
6. **Verify** broader build/integration/platform/security/release behavior only to the depth justified by the boundary and repository gates.
7. **Integrate** against fresh canonical state and prove the intended result survived.
8. **Document** only changed durable truth.
9. **Hand off** exact revision, evidence, risks, and next action.

## Add risk-specific stages when relevant

- Public/API/schema change: caller/consumer closure and compatibility/migration checks.
- Filesystem, database, deployment, migration, or destructive change: fault, rollback, idempotency, and recovery proof.
- Concurrency/async change: race, ordering, cancellation, retry, and lifetime checks.
- Security/auth/supply-chain change: threat-boundary and least-privilege verification.
- Performance change: measure representative behavior before and after.
- Release/update change: artifact identity, publication, install/update/rollback, and post-release verification.
- Cross-cutting/high-risk change: independent review or adversarial testing.

A trivial documentation correction does not need release-scale ceremony. A high-risk state mutation does not become safe because a unit test passed.
