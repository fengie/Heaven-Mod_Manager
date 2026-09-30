# GitHub commit evidence adapter — active PG-005 subtask

Owner: current process-scaling chat, branch `codex/ci-evidence-scaling`, base c0637afa. Existing release orchestration and confirmation boundaries remain owned by Heaven Workflows; this creates no duplicate network/auth/credential implementation.

The current runtime's GitHub commit-workflow query omitted main runs for 7edcd03f (and prior f7bbeb8d), while the authorized repository Actions API proved those runs existed. The immediate task completed via authorized REST. This adapter replaces that incomplete query coverage with bounded exact-source collection, current-result selection and truthful unavailable/partial evidence.

1. Implement a reader-injected Actions collector and machine-readable contract; validate repository/SHA/event/branch, identities, pagination, output bounds and two-minute freshness.
2. Fix any-success gate aggregation, and revalidate snapshot-backed publication decisions. Keep publication/cancellation external and explicitly authorized.
3. Exercise old-success/new-failure/pending/recovery histories, conflicting identities, count changes, empty/malformed/oversized responses, source/scope mismatch, absent/failing providers and expiry; prove actual canonical-main API metadata normalizes into a plan-bound decision.
4. Require exact-head unified Plugin Toolbox, Security, Workflow Feature and Agent Control budget checks; integrate and release the coherent root patch. If installed/runtime copies are replaced, smoke the replacement and run the canonical strict-version pruner immediately.
5. Wire only an already-authorized host reader when available. Never forward bearer values through queue/result/status payloads or mark an unconfigured provider available. Preserve separate Bridge-auth and actual installed-client gaps. Record exact current proof, next improvements and ownership before closure.

Acceptance: API failure never becomes an empty-success result; partial/ambiguous/running/expired evidence never satisfies a gate; an older successful run cannot mask a newer failed run; latest recovered success can pass when ordering is proven; exact repo/source/event/branch and publication confirmation stay bound. Future work: host-provider registration/health and workload measurement, not a second token store or speculative evidence cache.
