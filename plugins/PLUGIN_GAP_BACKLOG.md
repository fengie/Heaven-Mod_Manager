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

Managers should treat planned entries here as future-agent implementation candidates and claim them when dependency order and capacity permit.

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
- **Capability/API contract:** Generate/read a machine-readable toolbox index containing plugin/package name, capability names, task/intents/tags, platform/machine scope, safety/permission class, preferred precedence, validation command, and implementation status. Expose a bounded resolver such as `resolve_capabilities(required=[...], context={machine, repo, task_tags})` that returns ranked compatible capabilities plus explicit reasons; it must not silently execute anything.
- **Security / permission boundary:** Discovery is read-only. Do not expose secrets, plugin credentials, connector tokens, or hidden runtime configuration. Resolver output may describe required permissions but must not grant them.
- **Dependencies / reuse:** Reuse existing manifests/capability registries where available. Add validation that detects stale/missing manifest entries and duplicate providers without declared precedence.
- **Acceptance tests:** every implemented plugin package is represented; missing/stale entries fail verification; resolver selects the expected narrow capability for representative shell/filesystem/Git/build/browser/desktop/queue/indexing tasks; ambiguity is explicit rather than guessed; no secrets appear in generated index/output.
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

## PG-003 — repeatable startup/process performance workflow

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

