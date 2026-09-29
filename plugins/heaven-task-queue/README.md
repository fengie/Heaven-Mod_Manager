# Heaven Task Queue

SQLite-backed reusable queue for agent/worker coordination.

## Capabilities

- create/claim/renew/complete/fail/block/cancel tasks;
- dependency-aware scheduling;
- priority ordering;
- worker registration and heartbeats;
- bounded retry attempts and expiring leases;
- named resource locks to prevent two workers from mutating the same repo/resource concurrently;
- bounded JSON payload/result storage;
- hard provider usage-limit detection that marks the run `capacity_blocked` instead of healthy/retryable;
- a single operator notice (`Usage limit reached · agent stopped`) plus an immediate termination signal/callback;
- execution-mode capacity suppression so the same unavailable lane is not respawned until its capacity block is cleared or expires.

The queue uses `BEGIN IMMEDIATE` around claims and lock acquisition so two workers cannot claim the same task or resource concurrently.

## Hard usage limits

Create provider-backed agent work with a stable `capacity_scope` such as `codex`, `work`, or another execution mode. Route failed agent output through `handle_agent_failure(...)`. Hard quota/credit exhaustion is terminal: the task lease and owned resource locks are released, the task becomes `capacity_blocked`, the scope is suppressed from future claims, and the result returns `terminate_agent=true`. Callers that own the process/job can pass a `terminate` callback so the plugin kills it immediately; otherwise the orchestrator must honor the termination signal.

Transient stream/network/rate-limit failures continue through the normal retry path. Clearing a capacity block re-enables future tasks for that scope but does not resurrect the killed historical run.

## Security boundary

Do not place credentials or other secrets in queue payloads/results. Payloads are persisted to SQLite. Secret injection should remain host-side through the control plane/bridge secret-handle mechanisms.

## Validate

```powershell
python .\plugins\heaven-task-queue\verify.py
```
