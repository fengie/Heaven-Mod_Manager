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

**Outage escalation:** when the task-relevant plugin/connector/tool is proven unavailable in the current runtime, follow `LOCAL_REPLACEMENT_PROTOCOL.md`. The current agent must immediately create/update the durable plan, claim/start the local replacement project, and make the first concrete repository implementation move. Do not leave an outage-triggered item merely `PLANNED` when repository mutation is available. Reuse existing local primitives and create only the missing compatibility/adapter surface.

Managers should treat ordinary planned entries as future-agent implementation candidates. Outage-triggered entries are active work and should be `CLAIMED` (or `BLOCKED` only with exact external/dependency evidence). Preflight records must identify the runtime/session capability discovery they actually performed; stale tool lists or remembered availability do not count.

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

- **Status:** CLAIMED
- **Priority:** High
- **Triggering use case:** Agents sometimes reach for generic shell/manual control even though a purpose-built repository or ChatGPT plugin already exists. Correct routing currently depends too much on remembering package names and reading scattered documentation.
- **Why reusable:** Every engineering/computer-control task benefits from fast deterministic discovery of the narrowest existing capability before a fallback is chosen.
- **Existing capability audit:** `plugins/heaven-control-plane/` has runtime capability discovery for its own structured functions, `plugins/README.md` inventories major packages, and ChatGPT can enumerate installed plugins. There is no canonical repository-level machine-readable toolbox index that maps intents/task classes to owning plugin capabilities and precedence.
- **Proposed owner/plugin boundary:** Extend the plugin platform under `plugins/_tooling/` (or a small module owned by `plugins/heaven-control-plane/` if runtime integration is clearly superior). Do not create a second control plane.
- **Capability/API contract:** Generate/read a machine-readable toolbox index containing plugin/package name, capability names, task/intents/tags, platform/machine scope, safety/permission class, preferred precedence, validation command, and implementation status. Expose a bounded resolver such as `resolve_capabilities(required=[...], context={machine, repo, task_tags})` that returns ranked compatible capabilities plus explicit reasons, current-runtime availability evidence, and activation/read-instructions steps; it must not silently execute anything.
- **Security / permission boundary:** Discovery is read-only. Do not expose secrets, plugin credentials, connector tokens, or hidden runtime configuration. Resolver output may describe required permissions but must not grant them.
- **Dependencies / reuse:** Reuse existing manifests/capability registries where available. Add validation that detects stale/missing manifest entries and duplicate providers without declared precedence.
- **Acceptance tests:** every implemented plugin package is represented; missing/stale entries fail verification; resolver selects the expected narrow capability for representative shell/filesystem/Git/build/browser/desktop/queue/indexing tasks; ambiguity is explicit rather than guessed; representative preflight output records current-runtime discovery plus required activation/read-instructions steps; no secrets appear in generated index/output.
- **Owner / branch / PR:** current plugin-tooling lane; canonical-main integration.
- **Completion evidence:** pending.

## PG-002 — plugin-gap capture helper

- **Status:** CLAIMED
- **Priority:** Medium
- **Triggering use case:** The standing rule requires agents to preserve useful missing-plugin ideas in this repository, but hand-writing consistent entries is easy to skip during a busy implementation task.
- **Why reusable:** Every newly discovered toolbox gap needs the same duplicate checks, schema fields, and durable planning evidence.
- **Existing capability audit:** This Markdown backlog provides the policy and schema, but there is no helper that validates or creates a conforming entry.
- **Proposed owner/plugin boundary:** `plugins/_tooling/`; keep it development tooling, not a runtime machine-control plugin.
- **Capability/API contract:** A small CLI/library command such as `plugin-gap plan` that accepts title/use-case/required-capability/priority, searches the index/backlog for likely duplicates, emits a complete draft entry, and validates IDs/status/required fields. It must require an agent/human to choose whether a possible duplicate should be extended.
- **Security / permission boundary:** Repository-text only; no credentials or external account data. It may write only the canonical backlog (or a generated draft file) after normal repository authorization.
- **Dependencies / reuse:** Prefer PG-001's toolbox index when available; until then parse repository manifests/README metadata conservatively.
- **Acceptance tests:** deterministic ID/schema validation; duplicate candidate detection; refusal to overwrite an existing ID; generated entry contains every required field; repository verification fails malformed backlog entries.
- **Owner / branch / PR:** current plugin-tooling lane; canonical-main integration.
- **Completion evidence:** pending.


## PG-003 — secure secret handles and permission broker

- **Status:** CLAIMED
- **Priority:** High
- **Triggering use case:** System, release, SSH, package, browser, and API workflows increasingly need credentials, but the toolbox intentionally refuses to persist secrets in task payloads, state checkpoints, logs, or plugin metadata.
- **Why reusable:** Nearly every privileged workflow needs the same safe pattern for referencing a secret without copying its plaintext into durable state or agent-visible logs.
- **Existing capability audit:** Current plugins document “do not persist secrets” and use environment variables where possible, but there is no shared opaque secret-handle contract or capability-level permission broker.
- **Proposed owner/plugin boundary:** Add a shared security module under `plugins/_shared/` plus control-plane integration. Do not build a credential vault; adapt existing OS/user-authorized secret stores and connector-provided handles.
- **Capability/API contract:** `resolve_secret(handle, purpose, ttl)` returns only an ephemeral execution binding, never plaintext in serialized results; `authorize(capability, resource, mutation)` returns allow/deny plus reason; capabilities declare required permission classes in manifests.
- **Security / permission boundary:** No plaintext secret values in SQLite, logs, checkpoints, manifests, test fixtures, exceptions, or Git. Mutation authorization must fail closed. Secret handles are scoped, expiring where the backing store supports it, and non-exportable through normal plugin APIs.
- **Dependencies / reuse:** Reuse plugin manifests, Heaven Control Plane execution environment injection, OS credential facilities where available, and ChatGPT connector authorization when the action is connector-native.
- **Acceptance tests:** secret values never appear in serialized outputs/log capture; expired/unknown handles fail closed; capability permission declarations are validated; denied mutations do not reach transport; tests use fake handles only.
- **Owner / branch / PR:** owner = current plugin platform lane; branch = `plugin-security-broker-sync-20260929`; PR #290.
- **Completion evidence:** pending exact-head Heaven Plugin Toolbox Gate and canonical-main merge.

## PG-004 — distributed task queue and cluster integration

- **Status:** DONE
- **Priority:** High
- **Triggering use case:** `heaven-task-queue` can lease work and `heaven-cluster` can select machines, but they are currently independent building blocks.
- **Why reusable:** Every multi-machine agent/build/index/test workflow needs one consistent path from queued task -> compatible worker -> lease/capacity reservation -> execution receipt -> retry/release.
- **Existing capability audit:** Task dependencies, leases, retries, resource locks, worker heartbeat, capability-aware cluster selection, and atomic capacity reservation already exist separately.
- **Proposed owner/plugin boundary:** Extend `heaven-workflows` or add a thin orchestration module under `heaven-cluster`; do not duplicate queue or scheduler storage.
- **Capability/API contract:** a dispatcher loop claims one runnable task, derives required capabilities/labels/resources, atomically reserves a healthy worker, invokes a caller-supplied transport, records result/error/receipt, renews leases during long work, and always releases capacity/resources.
- **Security / permission boundary:** Queue payloads continue to forbid secrets. Dispatcher may use opaque secret handles only after PG-003. Worker endpoint/capability data is not itself authority to execute a privileged action.
- **Dependencies / reuse:** `heaven-task-queue`, `heaven-cluster`, `heaven-state-store`, and existing bridge/control-plane transports.
- **Acceptance tests:** race between two dispatchers never double-claims a task or over-reserves a worker; stale workers are skipped; lease expiry/retry works; cancellation releases reservations; execution receipts are durable and bounded.
- **Owner / branch / PR:** completed by queue/cluster integration lane; PR #288.
- **Completion evidence:** merged to canonical `main` as `07182c8d61edfe304b5d5394c43c6b6ac9653ae5`; exact-head Plugin Toolbox Gate run #54 (`36613089091`) passed on the Heaven self-hosted runner, including unified plugin verification and whitespace checks.

## PG-005 — CI and release orchestrator

- **Status:** CLAIMED
- **Priority:** High
- **Triggering use case:** Agents repeatedly need to identify the exact candidate commit, run the correct release gates, wait for self-hosted/hosted checks, publish artifacts, verify release identity, and clean superseded runs/branches.
- **Why reusable:** This is a recurring repository workflow with high consequences when commit identity, artifact provenance, or release ordering is wrong.
- **Existing capability audit:** Git integration, workflow verification, state/artifact storage, process execution, and repository-specific release workflows exist, but there is no reusable release state machine.
- **Proposed owner/plugin boundary:** New specialized `heaven-release` plugin only if the contract remains repository-agnostic; otherwise extend `heaven-workflows` with a release module and repository adapters.
- **Capability/API contract:** plan release -> pin exact commit -> run named gates -> verify required statuses belong to that commit -> build/publish artifacts -> verify hashes/version/channel -> record receipt -> optionally cancel superseded runs. Every destructive/publishing step requires explicit confirmation.
- **Security / permission boundary:** Never embeds tokens; uses PG-003 secret handles/authorized connectors. Cannot bypass branch protection, workflow permissions, signing policy, or platform quotas.
- **Dependencies / reuse:** `heaven-git-ops`, `heaven-workflows`, `heaven-state-store`, GitHub connector/actions, updater/release repository conventions.
- **Acceptance tests:** rejects status from wrong commit; refuses publish without confirmed candidate; artifact hash/version mismatch blocks completion; idempotent rerun recognizes already-published identical release; receipt records exact commit and artifact hashes.
- **Owner / branch / PR:** canonical-main recovery from closed PR #416 / preservation-merge recovery PR #424; verification lane pending.
- **Completion evidence:** implementation/tests/manifest/README restored to canonical `main`; exact-main Plugin Toolbox Gate + Security Supply Chain Gate still required before marking DONE.

## PG-006 — deep browser automation adapter

- **Status:** CLAIMED
- **Priority:** Medium
- **Triggering use case:** `heaven-browser` can launch/navigate via desktop/UIA, but DOM selectors, downloads, console/network inspection, multi-tab control, and deterministic page-state waits are still missing.
- **Why reusable:** Browser-heavy workflows are safer and more reliable when semantic DOM/browser protocols replace coordinate/UIA fallbacks.
- **Existing capability audit:** Desktop/browser UIA automation and screenshots are implemented; ChatGPT also has external browser automation plugins for supported environments. No local Heaven CDP/Playwright adapter exists yet.
- **Proposed owner/plugin boundary:** Extend `heaven-browser`; do not create a second browser package. Prefer Playwright or CDP with an explicit browser-session ownership model.
- **Capability/API contract:** create/attach session, navigate, query DOM, click/fill non-secret fields, wait for selector/network-idle, manage tabs, download to an allowed root, capture console/network summaries, screenshot, and close owned sessions.
- **Security / permission boundary:** Restrict URL schemes and download roots; secret field fill requires PG-003 handles; do not bypass authentication, anti-bot systems, CAPTCHAs, or site permissions; clearly distinguish owned browser sessions from arbitrary user windows.
- **Dependencies / reuse:** `heaven-browser`, `heaven-desktop`, process/services, file ops, visual preprocessing, optional Playwright/CDP runtime.
- **Acceptance tests:** deterministic local fixture page exercises navigation/selectors/forms/tabs/downloads; path traversal rejected; owned-session cleanup verified; sensitive values absent from logs/screenshots where redaction is requested.
- **Owner / branch / PR:** `heaven-browser` deep-provider lane; implementation integrated on canonical `main`, live Playwright/Brave fixture verification pending.
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


## PG-008 — bridge self-healing host persistence

- **Status:** CLAIMED
- **Priority:** Critical
- **Triggering use case:** The `heaven2` bridge worker went offline, which made the purpose-built desktop/control plugin unavailable precisely when it was needed to repair the machine.
- **Why reusable:** Every persistent local control plane needs recovery that does not depend on the failed control-plane process itself.
- **Existing capability audit:** Heaven Local Bridge already had a worker scheduled task, finite restart-on-failure, Startup fallback, remote relay heartbeat, and hardened `RECOVER`; it lacked an independent watchdog, Git-independent liveness/progress signals, indefinite task execution, and a startup handoff that could not race the elevated worker.
- **Proposed owner/plugin boundary:** Extend the existing `heaven-bridge/` runtime and `heaven-local-bridge` plugin metadata. Do not create a second bridge/control-plane implementation.
- **Capability/API contract:** bootstrap installs/refreshed canonical worker + watchdog persistence; worker publishes local process heartbeat and queue-loop progress; watchdog starts/restarts the exact worker without Git/relay dependency; `STATUS` proves worker/watchdog/runtime/task/local/remote health; `STOP` disables watchdog before intentional worker shutdown.
- **Security / permission boundary:** Preserve the existing interactive-user/RunLevel Highest boundary; do not expose new network listeners, credentials, or relay secrets. Startup fallback prefers the elevated scheduled watchdog and uses direct non-elevated launch only when Task Scheduler cannot provide the persistent owner.
- **Dependencies / reuse:** Existing worker singleton, Task Scheduler bootstrap, Startup folder, relay heartbeat, `manage.ps1`, and plugin health semantics.
- **Acceptance tests:** PowerShell parse; worker unit/compatibility suites; exact-head Heaven Local Bridge gate; task settings prove no 72-hour execution limit and maximum restart count; watchdog source contains no Git dependency; live worker-kill recovery; live watchdog-kill/restart recovery; `STATUS` green on both `heaven2` and `heaven`.
- **Owner / branch / PR:** owner = current reliability lane; branch = `fix/heaven2-bridge-self-heal-20260929`; PR #230.
- **Completion evidence:** pending exact-head gate, canonical-main integration, transport sync, and live two-host recovery verification.


## PG-009 — local Remote Desktop Commander compatibility replacement

- **Status:** CLAIMED
- **Priority:** Critical
- **Triggering use case:** On 2026-09-29 the Remote Desktop Commander connector still reported registered/online devices but command execution was usage-paused, blocking a storage audit on `heaven2` while the host itself remained present.
- **Why reusable:** Local machine administration must not depend on one external connector's usage window when equivalent authorized local capabilities already exist.
- **Existing capability audit:** Heaven Local Bridge plus `heaven-control-plane`, `heaven-file-ops`, `heaven-process-services`, `heaven-desktop`, `heaven-browser`, and `heaven-system-ops` already implement most underlying primitives. The missing boundary is a stable compatibility/parity package, routing metadata, and explicit workflow parity so agents can switch locally without inventing ad-hoc commands.
- **Proposed owner/plugin boundary:** `plugins/remote-desktop-commander-local/`. It owns compatibility mapping, routing/discovery metadata, parity tracking, and only thin missing adapters. It must not fork the existing bridge/desktop/filesystem/process implementations.
- **Capability/API contract:** Map external-style device/filesystem/process/desktop/system operations to local providers; first concrete workflow is a bounded read-only storage audit returning drive usage, top directories, large caches/build roots, and largest files with explicit access-denied/error evidence. Health must distinguish host presence from transport readiness.
- **Security / permission boundary:** Operates only through existing user-authorized Heaven control paths. No public listener, no secret persistence, no third-party quota/authentication bypass, and no widening of machine authority.
- **Dependencies / reuse:** Existing `heaven-bridge/` transport and the Heaven plugin packages listed above. Follow `plugins/LOCAL_REPLACEMENT_PROTOCOL.md`.
- **Acceptance tests:** storage audit succeeds on `heaven2` and `heaven` without Remote Desktop Commander; unavailable external connector does not cause retry storms; provider selection records current-runtime evidence; output is bounded; access failures are explicit; host presence and transport health remain separate; no secrets appear in results/logs.
- **Owner / branch / PR:** owner = current local-replacement lane; project path = `plugins/remote-desktop-commander-local/`; integrated scaffold lives directly on canonical `main`.
- **Completion evidence:** project README, implementation plan, and machine-readable replacement manifest created on 2026-09-29; M1 storage-audit workflow and routing integration remain active.
