# Heaven Workflows

## v0.4.0 — current scoped CI evidence

`release_fetch_gates` and `release_verify_github_gates` read bounded repository Actions evidence through a caller-injected authorized JSON reader. They bind exact repository, SHA, event and branch, disclose incomplete pagination, retain only a proven newest workflow result, and expire after two minutes. Generic gate verification now rejects conflicting same-gate rows and running states instead of accepting any historical success. Snapshot-backed publication authorization revalidates its evidence; it still emits a receipt and performs no publication itself.

The upstream commit-workflow connector omitted healthy main runs during v8.8.41 closure. This adapter extends the existing owner and requires an authorized reader; it does not create a credential store or transport. Missing/failing readers are explicit errors, not empty-success results. See `PLAN.md` and `contracts/github-commit-evidence.v1.json`. The [official repository workflow-runs API](https://docs.github.com/en/rest/actions/workflow-runs#list-workflow-runs-for-a-repository) supplies head/event/branch filters and pagination.

```python
# read_json must be an already-authorized, fresh, timeout/response-bounded
# machine-local GitHub Actions GET provider. Keep credentials inside it.
plugin = HeavenWorkflowPlugin(control_plane, github_reader=read_json)
evidence = plugin.release_fetch_gates("fengie/mhw-mods", exact_sha,
                                     event="push", branch="main")
result = plugin.release_verify_github_gates(plan, evidence)
```

Provider configuration remains a host integration boundary; source registration alone does not configure authentication or make a bridge healthy. For partial or changing results, refresh/narrow the authorized query instead of treating missing observations as pass or retrying indefinitely. Ambiguous workflow names/creation order fail closed. No completed evidence cache is retained.

Reusable high-level workflows built on top of `heaven-control-plane` rather than duplicating bridge/runtime logic.

## Capabilities

- `workflow.repo_snapshot` — Git status, optional diff, and exact remote-main identity.
- `workflow.verify_repo` — project detection followed by bounded fixed test/build/lint/typecheck plans.
- `workflow.parallel_invoke` — run up to 16 independent **allowlisted structured capabilities** concurrently with at most 8 workers.
- `workflow.release.plan` — pin exact source SHA, version/channel, required gates, and expected artifacts into a deterministic plan ID.
- `workflow.release.verify_gates` — reject missing, failed, or wrong-commit gate evidence.
- `workflow.release.fetch_gates` — fetch bounded current Actions metadata through the configured authorized reader.
- `workflow.release.verify_github_gates` — bind complete unexpired repo/source/event/branch evidence to the release plan.
- `workflow.release.verify_artifacts` — bind digest, size, version, and channel before publication.
- `workflow.release.authorize_publish` — require an exact plan-bound confirmation phrase and emit a durable receipt without publishing by itself.
- `workflow.release.reconcile_existing` — recognize an identical existing release as already published and reject conflicting release identity.
- `workflow.release.plan_cancellations` — identify superseded active workflow runs while leaving cancellation as a separately confirmed mutation.
- `workflow.change.status` — read the durable optimistic checkpoint for a declared change.
- `workflow.change.prepare` — pin an exact pre-state plus explicit reversible steps before mutation.
- `workflow.change.commit` — bind the final post-state ID into a deterministic commit receipt.
- `workflow.change.rollback` — execute only registered inverse capabilities in reverse order with retry-safe per-step checkpoints.

`parallel_invoke` intentionally refuses raw `execution.run`; it is for parallelizing structured, already-bounded control-plane operations.

## Security boundary

The plugin never handles credentials directly. It delegates permission enforcement and transport to the supplied control plane, rejects repository traversal segments, and refuses arbitrary raw commands in its scheduler. Rollback plans reject raw execution capabilities and credential-bearing persisted fields, accept opaque `*_handle` identifiers only, require explicit confirmation before inverse mutations, and checkpoint each completed inverse so retries do not repeat already-successful steps. Release orchestration fails closed on commit, gate, digest, version, channel, or confirmation mismatch; publication and workflow cancellation remain external confirmed mutations.

## Validate

```powershell
python .\plugins\heaven-workflows\verify.py
```
