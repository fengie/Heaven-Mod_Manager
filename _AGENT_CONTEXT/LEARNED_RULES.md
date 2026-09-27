# Learned Rules — append-only agent ledger

Core Rules live in `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`. This file records durable, incident-driven rules discovered while working on `fengie/mhw-mods`.

## Ledger discipline

- Preserve append-only history; do not silently erase old rules.
- Status is `Active` or `Superseded`.
- When replacing a rule, add the replacement and mark/link the old rule as Superseded.
- Add a rule only when a concrete incident or discovery makes recurrence likely and the rule materially improves correctness, safety, or engineering time.
- Do not add session-specific observations or duplicate Core Rules.
- Learned Rules may extend Core Rules but may never weaken or contradict them.

Each rule records: Rule ID, status, date, scope, rule, trigger/evidence, rationale, enforcement, relevant commit/run when useful, and supersession links.

---

## LR-001 — moved production bodies require verification-instrumentation re-audit

- **Rule ID:** LR-001
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Production C# moves/extractions and function verification
- **Rule:** When a production method/body is moved, extracted, or materially regenerated, re-audit its required `MasterDebugLog.BeginMethod()` entry instrumentation and resulting function fingerprint/call-site coverage before treating the slice as verification-ready.
- **Trigger / evidence:** Hosted Windows run `36330808544` reached 24/25 and found exactly one missing trace in moved `MainWindowViewModel.ScanInstalledGames()`, leaving seven explicit call sites uncovered. Production fix `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34` restored the trace; run `36331057943` later closed the slice.
- **Rationale:** A behavior-preserving move can create a new verification fingerprint and lose required runtime instrumentation even when method logic is unchanged.
- **Enforcement:** Review moved/new production bodies during each one-seam checkpoint; preserve function-verifier regression coverage; never manually promote verification caches.
- **Relevant commit/run:** `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`; runs `36330808544`, `36331057943`
- **Supersedes:** none
- **Superseded by:** none


## LR-002 — shared API removal requires compile-backed caller closure

- **Rule ID:** LR-002
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Shared facade/API extraction and caller migration
- **Rule:** When removing or relocating a callable shared API, do not treat indexed/code-search results as an exhaustive caller list. Use repository-wide textual inspection when available and require a whole-solution compile before declaring caller migration complete.
- **Trigger / evidence:** PlannerSnapshotRepository candidate run `36335255922` failed because `NexusMetadataService` and `GameBuildMonitor` still called the removed `ManagerDatabase.LoadPlannerSnapshotAsync`, even though the connector's private-repository code search had returned no matches for that symbol.
- **Rationale:** Private-repository search indexes/connectors can be incomplete or stale. A removed shared API turns any missed caller into a compile break; the compiler is the authoritative closure check.
- **Enforcement:** For API-removal/extraction checkpoints, combine static caller inspection with strict whole-solution compilation before calling the migration complete. Preserve LR-001 trace re-audit separately for moved/changed production bodies.
- **Relevant commit/run:** repair `528401925b1d09b3d65c9652de8e4f2024e3677f`; failed run `36335255922`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-005 — restartable migrations must prove ownership and convergence

- **Rule ID:** LR-005
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** one-shot/restartable data migrations spanning SQLite and filesystem state
- **Rule:** A migration that treats partial destination state as disposable must establish migration ownership before destructive reset, verify pre-existing destination artifacts before trusting/reusing them, and distinguish cleanup attempted from cleanup proven complete. Every failed attempt must either converge automatically on retry or surface a precise non-retryable recovery action.
- **Trigger / evidence:** The legacy v7→v8 migration audit found that a corrupt pre-existing hash-named CAS object is reused by existence, then rejected only at final SHA verification; failure reset removes the DB blob row but leaves the physical object, so the same retry can fail indefinitely. The same audit found that migration cleanup exceptions are swallowed while the returned message still claims partial rows were discarded, and that marker absence alone authorizes broad reset of imported v8 state.
- **Rationale:** Restart protocols are only safe when retries make progress and when destructive cleanup is scoped to state proven to belong to the incomplete migration. A single marker or filename is not sufficient ownership/integrity evidence.
- **Enforcement:** New migration work must include characterization/fault tests for ownership, artifact verification, cleanup outcome, cancellation/process death, and retry convergence before broad protocol changes. Preserve legacy/source authority until completion is proven. Do not report cleanup as complete when recovery itself failed.
- **Relevant audit:** `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md`
- **Parallel rule numbering note:** LR-003 and LR-004 are already reserved by open support PRs #4 and #7; LR-005 avoids a future merge collision even though canonical main currently contains only LR-001/LR-002.
- **Supersedes:** none
- **Superseded by:** none

