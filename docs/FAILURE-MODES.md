# Failure modes — hardened behavior

| Failure | Required behavior |
|---|---|
| Kill before journal | No live mutation has occurred. Orphan CAS captures are harmless and later GC/quarantine may reclaim them. |
| Kill after journal, before first write | Startup rolls the prepared operation back to a no-op/before-state. |
| Kill after one or more file writes | Startup compares each live path to journal before/after hashes and restores the before-image. |
| Kill after all writes, before DB commit | `FilesWritten`/`StateCommitting` is incomplete; recovery restores the before-image. |
| Kill after DB commit | Operation is `Committed`; live files and durable ownership state remain the after-image. |
| External edit after crash | If live bytes match neither journal image, stop with `RecoveryRequired`; never destroy the edit. |
| Stale plan before apply | Whole-plan preflight fails before any file mutation. |
| File changes between preflight and its write | Per-file recheck fails and journaled earlier writes roll back. |
| Disk full | File operation fails; durable journal drives rollback. |
| Permission/sharing failure | Report affected path and Restart Manager process ownership when available; rollback. |
| Same-size/same-timestamp source edit | XXH3 cache verification detects changed content and recaptures authoritative SHA-256 blob. |
| Manual replacement of managed file | Expected-live hash fails; do not write through the user's change. |
| File/directory shape collision | Detect before mutation; never recursively delete unknown content to make the desired shape fit. |
| Watcher overflow/lost events | Record the condition; watcher is only a hint and correctness falls back to authoritative reconciliation/preconditions. |
| SQLite BUSY/LOCKED | Short pooled operations honor busy timeout; do not hold DB transactions across filesystem I/O. |
| SQLite corruption | Health/support records `integrity_check`; do not auto-delete DB/legacy/live files. |
| Missing CAS blob | Fail closed and identify references. |
| Cyclic overlay precedence | Planner emits no deployment and surfaces the cycle. |
| Archive traversal/device/ADS/reparse path | Reject before extraction outside staging. |
| MHW/another process locks target | Do not auto-kill; surface process/PID where Restart Manager can identify it. |
| Dispatcher stall | Watchdog records delay + active operations; do not auto-restart. |
