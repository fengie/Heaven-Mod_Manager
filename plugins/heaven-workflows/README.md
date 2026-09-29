# Heaven Workflows

Reusable high-level workflows built on top of `heaven-control-plane` rather than duplicating bridge/runtime logic.

## Capabilities

- `workflow.repo_snapshot` — Git status, optional diff, and exact remote-main identity.
- `workflow.verify_repo` — project detection followed by bounded fixed test/build/lint/typecheck plans.
- `workflow.parallel_invoke` — run up to 16 independent **allowlisted structured capabilities** concurrently with at most 8 workers.

`parallel_invoke` intentionally refuses raw `execution.run`; it is for parallelizing structured, already-bounded control-plane operations.

## Security boundary

The plugin never handles credentials directly. It delegates permission enforcement and transport to the supplied control plane, rejects repository traversal segments, and refuses arbitrary raw commands in its scheduler.

## Validate

```powershell
python .\plugins\heaven-workflows\verify.py
```
