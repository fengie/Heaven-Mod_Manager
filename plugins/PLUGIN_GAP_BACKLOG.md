# Plugin Gap Backlog

This is the canonical durable backlog for reusable plugin/toolbox capabilities discovered while doing real work.

## Standing rule

Agents must prefer the correct existing plugin/capability before generic shell, browser, desktop, API, or manual fallbacks.

When a task reveals a reusable capability that would make the same class of work safer, faster, more reliable, less manual, or more token-efficient:

1. search existing plugin packages, this backlog, active branches, and open PRs for an existing owner/plan;
2. use or extend the natural existing plugin when possible instead of creating a duplicate;
3. if the capability is missing or materially incomplete, add/update a plan here immediately;
4. continue the current user task through the safest authorized fallback when possible;
5. do not leave the plugin idea only in chat, memory, or a transient handoff.

Managers should treat planned entries here as future-agent implementation candidates and claim them when dependency order and capacity permit. Preflight records must identify the runtime/session capability discovery they actually performed; stale tool lists or remembered availability do not count.

## Status values

- `PLANNED` — implementation-ready plan exists; no active owner.
- `CLAIMED` — an active owner/branch/lease is implementing it.
- `BLOCKED` — implementation is owned but stopped by a named external/dependency gate.
- `DONE` — capability is verified on remote `main`; implementation evidence is linked.
- `SUPERSEDED` — replaced by another capability/entry; link the replacement.

## Required entry schema

Each entry must include:

- **ID / title**
- **Status**
- **Priority**
- **Triggering use case**
- **Why reusable**
- **Existing capability audit**
- **Proposed owner/plugin boundary**
- **Capability/API contract**
- **Security / permission boundary**
- **Dependencies / reuse**
- **Acceptance tests**
- **Owner / branch / PR** when claimed
- **Completion evidence** when done

Do not create a new plugin when extending an existing plugin/control-plane module gives a cleaner ownership boundary.

---

## PG-001 — toolbox capability index and task router

- **Status:** PLANNED
- **Priority:** High
- **Triggering use case:** Agents sometimes reach for generic shell/manual control even though a purpose-built repository or ChatGPT plugin already exists. Correct routing currently depends too much on remembering package names and reading scattered documentation.
- **Why reusable:** Every engineering/computer-control task benefits from fast deterministic discovery of the narrowest existing capability before a fallback is chosen.
- **Existing capability audit:** `plugins/heaven-control-plane/` has runtime capability discovery for its own structured functions, `plugins/README.md` inventories major packages, and ChatGPT can enumerate installed plugins. There is no canonical repository-level machine-readable toolbox index that maps intents/task classes to owning plugin capabilities and precedence.
- **Proposed owner/plugin boundary:** Extend the plugin platform under `plugins/_tooling/` (or a small module owned by `plugins/heaven-control-plane/` if runtime integration is clearly superior). Do not create a second control plane.
- **Capability/API contract:** Generate/read a machine-readable toolbox index containing plugin/package name, capability names, task/intents/tags, platform/machine scope, safety/permission class, preferred precedence, validation command, and implementation status. Expose a bounded resolver such as `resolve_capabilities(required=[...], context={machine, repo, task_tags})` that returns ranked compatible capabilities plus explicit reasons, current-runtime availability evidence, and activation/read-instructions steps; it must not silently execute anything.
- **Security / permission boundary:** Discovery is read-only. Do not expose secrets, plugin credentials, connector tokens, or hidden runtime configuration. Resolver output may describe required permissions but must not grant them.
- **Dependencies / reuse:** Reuse existing manifests/capability registries where available. Add validation that detects stale/missing manifest entries and duplicate providers without declared precedence.
- **Acceptance tests:** every implemented plugin package is represented; missing/stale entries fail verification; resolver selects the expected narrow capability for representative shell/filesystem/Git/build/browser/desktop/queue/indexing tasks; ambiguity is explicit rather than guessed; representative preflight output records current-runtime discovery plus required activation/read-instructions steps; no secrets appear in generated index/output.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.

## PG-002 — plugin-gap capture helper

- **Status:** PLANNED
- **Priority:** Medium
- **Triggering use case:** The standing rule requires agents to preserve useful missing-plugin ideas in this repository, but hand-writing consistent entries is easy to skip during a busy implementation task.
- **Why reusable:** Every newly discovered toolbox gap needs the same duplicate checks, schema fields, and durable planning evidence.
- **Existing capability audit:** This Markdown backlog provides the policy and schema, but there is no helper that validates or creates a conforming entry.
- **Proposed owner/plugin boundary:** `plugins/_tooling/`; keep it development tooling, not a runtime machine-control plugin.
- **Capability/API contract:** A small CLI/library command such as `plugin-gap plan` that accepts title/use-case/required-capability/priority, searches the index/backlog for likely duplicates, emits a complete draft entry, and validates IDs/status/required fields. It must require an agent/human to choose whether a possible duplicate should be extended.
- **Security / permission boundary:** Repository-text only; no credentials or external account data. It may write only the canonical backlog (or a generated draft file) after normal repository authorization.
- **Dependencies / reuse:** Prefer PG-001's toolbox index when available; until then parse repository manifests/README metadata conservatively.
- **Acceptance tests:** deterministic ID/schema validation; duplicate candidate detection; refusal to overwrite an existing ID; generated entry contains every required field; repository verification fails malformed backlog entries.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.


## PG-003 — secure secret handles and permission broker

- **Status:** PLANNED
- **Priority:** High
- **Triggering use case:** System, release, SSH, package, browser, and API workflows increasingly need credentials, but the toolbox intentionally refuses to persist secrets in task payloads, state checkpoints, logs, or plugin metadata.
- **Why reusable:** Nearly every privileged workflow needs the same safe pattern for referencing a secret without copying its plaintext into durable state or agent-visible logs.
- **Existing capability audit:** Current plugins document “do not persist secrets” and use environment variables where possible, but there is no shared opaque secret-handle contract or capability-level permission broker.
- **Proposed owner/plugin boundary:** Add a shared security module under `plugins/_shared/` plus control-plane integration. Do not build a credential vault; adapt existing OS/user-authorized secret stores and connector-provided handles.
- **Capability/API contract:** `resolve_secret(handle, purpose, ttl)` returns only an ephemeral execution binding, never plaintext in serialized results; `authorize(capability, resource, mutation)` returns allow/deny plus reason; capabilities declare required permission classes in manifests.
- **Security / permission boundary:** No plaintext secret values in SQLite, logs, checkpoints, manifests, test fixtures, exceptions, or Git. Mutation authorization must fail closed. Secret handles are scoped, expiring where the backing store supports it, and non-exportable through normal plugin APIs.
- **Dependencies / reuse:** Reuse plugin manifests, Heaven Control Plane execution environment injection, OS credential facilities where available, and ChatGPT connector authorization when the action is connector-native.
- **Acceptance tests:** secret values never appear in serialized outputs/log capture; expired/unknown handles fail closed; capability permission declarations are validated; denied mutations do not reach transport; tests use fake handles only.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.

## PG-004 — distributed task queue and cluster integration

- **Status:** PLANNED
- **Priority:** High
- **Triggering use case:** `heaven-task-queue` can lease work and `heaven-cluster` can select machines, but they are currently independent building blocks.
- **Why reusable:** Every multi-machine agent/build/index/test workflow needs one consistent path from queued task -> compatible worker -> lease/capacity reservation -> execution receipt -> retry/release.
- **Existing capability audit:** Task dependencies, leases, retries, resource locks, worker heartbeat, capability-aware cluster selection, and atomic capacity reservation already exist separately.
- **Proposed owner/plugin boundary:** Extend `heaven-workflows` or add a thin orchestration module under `heaven-cluster`; do not duplicate queue or scheduler storage.
- **Capability/API contract:** a dispatcher loop claims one runnable task, derives required capabilities/labels/resources, atomically reserves a healthy worker, invokes a caller-supplied transport, records result/error/receipt, renews leases during long work, and always releases capacity/resources.
- **Security / permission boundary:** Queue payloads continue to forbid secrets. Dispatcher may use opaque secret handles only after PG-003. Worker endpoint/capability data is not itself authority to execute a privileged action.
- **Dependencies / reuse:** `heaven-task-queue`, `heaven-cluster`, `heaven-state-store`, and existing bridge/control-plane transports.
- **Acceptance tests:** race between two dispatchers never double-claims a task or over-reserves a worker; stale workers are skipped; lease expiry/retry works; cancellation releases reservations; execution receipts are durable and bounded.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.

## PG-005 — CI and release orchestrator

- **Status:** PLANNED
- **Priority:** High
- **Triggering use case:** Agents repeatedly need to identify the exact candidate commit, run the correct release gates, wait for self-hosted/hosted checks, publish artifacts, verify release identity, and clean superseded runs/branches.
- **Why reusable:** This is a recurring repository workflow with high consequences when commit identity, artifact provenance, or release ordering is wrong.
- **Existing capability audit:** Git integration, workflow verification, state/artifact storage, process execution, and repository-specific release workflows exist, but there is no reusable release state machine.
- **Proposed owner/plugin boundary:** New specialized `heaven-release` plugin only if the contract remains repository-agnostic; otherwise extend `heaven-workflows` with a release module and repository adapters.
- **Capability/API contract:** plan release -> pin exact commit -> run named gates -> verify required statuses belong to that commit -> build/publish artifacts -> verify hashes/version/channel -> record receipt -> optionally cancel superseded runs. Every destructive/publishing step requires explicit confirmation.
- **Security / permission boundary:** Never embeds tokens; uses PG-003 secret handles/authorized connectors. Cannot bypass branch protection, workflow permissions, signing policy, or platform quotas.
- **Dependencies / reuse:** `heaven-git-ops`, `heaven-workflows`, `heaven-state-store`, GitHub connector/actions, updater/release repository conventions.
- **Acceptance tests:** rejects status from wrong commit; refuses publish without confirmed candidate; artifact hash/version mismatch blocks completion; idempotent rerun recognizes already-published identical release; receipt records exact commit and artifact hashes.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.

## PG-006 — deep browser automation adapter

- **Status:** PLANNED
- **Priority:** Medium
- **Triggering use case:** `heaven-browser` can launch/navigate via desktop/UIA, but DOM selectors, downloads, console/network inspection, multi-tab control, and deterministic page-state waits are still missing.
- **Why reusable:** Browser-heavy workflows are safer and more reliable when semantic DOM/browser protocols replace coordinate/UIA fallbacks.
- **Existing capability audit:** Desktop/browser UIA automation and screenshots are implemented; ChatGPT also has external browser automation plugins for supported environments. No local Heaven CDP/Playwright adapter exists yet.
- **Proposed owner/plugin boundary:** Extend `heaven-browser`; do not create a second browser package. Prefer Playwright or CDP with an explicit browser-session ownership model.
- **Capability/API contract:** create/attach session, navigate, query DOM, click/fill non-secret fields, wait for selector/network-idle, manage tabs, download to an allowed root, capture console/network summaries, screenshot, and close owned sessions.
- **Security / permission boundary:** Restrict URL schemes and download roots; secret field fill requires PG-003 handles; do not bypass authentication, anti-bot systems, CAPTCHAs, or site permissions; clearly distinguish owned browser sessions from arbitrary user windows.
- **Dependencies / reuse:** `heaven-browser`, `heaven-desktop`, process/services, file ops, visual preprocessing, optional Playwright/CDP runtime.
- **Acceptance tests:** deterministic local fixture page exercises navigation/selectors/forms/tabs/downloads; path traversal rejected; owned-session cleanup verified; sensitive values absent from logs/screenshots where redaction is requested.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.

## PG-007 — rollback, restore, and scheduled maintenance workflows

- **Status:** PLANNED
- **Priority:** Medium
- **Triggering use case:** Long-running autonomous development and machine administration need safe checkpoints before mutations plus recurring cleanup/health/recovery actions.
- **Why reusable:** Git changes, service changes, package installs, updater operations, and distributed workers all benefit from a standard rollback receipt and bounded scheduler.
- **Existing capability audit:** State checkpoints/artifacts, Git integration, service/process control, package/system ops, and health history now exist independently. There is no cross-plugin rollback contract or local scheduled-job owner.
- **Proposed owner/plugin boundary:** Extend `heaven-workflows` for rollback plans and add a narrowly scoped scheduler module only if existing OS/task scheduling cannot be wrapped cleanly by process/services.
- **Capability/API contract:** `prepare_change()` records preconditions/checkpoint; `commit_change()` records final receipt; `rollback_change()` invokes only declared reversible steps. Scheduler supports bounded named recurring jobs, enable/disable/list/run-now, missed-run policy, and exact ownership.
- **Security / permission boundary:** Rollback never invents inverse commands; only explicitly declared reversible actions are eligible. Scheduled privileged jobs require the same permission checks as interactive execution and may reference only opaque secret handles.
- **Dependencies / reuse:** `heaven-state-store`, `heaven-workflows`, `heaven-process-services`, `heaven-git-ops`, `heaven-system-ops`.
- **Acceptance tests:** failed multi-step mutation triggers only registered rollback steps in reverse order; idempotent rollback; scheduler cannot create duplicate ownership for same job; disabled jobs never execute; receipts include exact pre/post state identifiers.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.

## PG-008 — repeatable startup/process performance workflow

- **Status:** PLANNED
- **Priority:** High
- **Triggering use case:** The startup-optimization task needed repeatable cold-state/warm-state Windows measurements, per-stage startup timings, CPU time, working-set/peak RAM, package size, process cleanup, and durable evidence on `heaven`. No exposed purpose-built Heaven plugin capability could perform this directly, so the task had to add a bespoke repository script/workflow.
- **Why reusable:** Startup regressions, launch-time investigations, packaging-bloat work, and before/after optimization checks all need the same controlled process lifecycle and evidence format. Rebuilding this orchestration per task is slow and risks inconsistent measurement.
- **Existing capability audit:** `plugins/heaven-workflows/` can compose verification and parallel read-only capabilities; `plugins/heaven-process-services/` and the Heaven control plane provide process execution/lifecycle primitives; `scripts/Measure-StartupPerformance.ps1` provides the first repository-specific measurement harness. There is no high-level performance workflow that owns iterations, cold/warm fixture setup, process-scoped telemetry, aggregation, or evidence publication.
- **Proposed owner/plugin boundary:** Extend `plugins/heaven-workflows/` with reusable performance workflow methods. Reuse process/observability/control-plane primitives rather than creating another execution plugin. Keep application-specific fixture preparation in repository scripts/config passed to the workflow.
- **Capability/API contract:** Add a bounded capability such as `performance.startup.measure` / `measure_startup(repo, target_host='heaven', launch, ready_probe, fixture_setup, cold_iterations, warm_iterations, timeout_seconds, metrics=[...])`. It should launch only through an owned process handle, sample CPU/memory/I/O where supported, collect application-reported stage timings/artifacts, compute per-run plus median/p95 summaries, distinguish manager-state cold from true OS-cache-cold claims, and return exact source SHA + machine/runtime provenance.
- **Security / permission boundary:** Execution is limited to an authorized repository/worktree and explicit target host. Kill/cleanup authority is restricted to processes spawned/leased by the workflow. Do not read or emit secrets. Network access is opt-in and must be visible in the measurement configuration. Never flush system caches, alter global performance settings, or kill unrelated processes without a separate explicit privileged capability.
- **Dependencies / reuse:** Reuse `heaven-process-services`, Heaven control-plane process/observability primitives, repository verification detection, existing artifact/log paging, and the startup diagnostic JSON emitted by `StartupDiagnosticSession`. The initial repository adapter may call `scripts/Measure-StartupPerformance.ps1` while the generic API is implemented.
- **Acceptance tests:** deterministic aggregation from fixture runs; owned-process cleanup on pass/fail/timeout; timeout does not kill unrelated processes; exact SHA/host/.NET provenance returned; cold/warm semantics labeled honestly; unsupported metrics return explicit N/A rather than fabricated values; a representative WPF startup fixture produces 3+ iterations and durable JSON evidence; plugin verification covers malformed launch/fixture inputs and path traversal.
- **Owner / branch / PR:** unclaimed.
- **Completion evidence:** pending.


