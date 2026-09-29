# Heaven Task Queue

SQLite-backed reusable queue for agent/worker coordination.

## Capabilities

- create/claim/renew/complete/fail/block/cancel tasks;
- dependency-aware scheduling;
- priority ordering;
- worker registration and heartbeats;
- bounded retry attempts and expiring leases;
- named resource locks to prevent two workers from mutating the same repo/resource concurrently;
- bounded JSON payload/result storage.

The queue uses `BEGIN IMMEDIATE` around claims and lock acquisition so two workers cannot claim the same task or resource concurrently.

## Security boundary

Do not place credentials or other secrets in queue payloads/results. Payloads are persisted to SQLite. Secret injection should remain host-side through the control plane/bridge secret-handle mechanisms.

## Validate

```powershell
python .\plugins\heaven-task-queue\verify.py
```
