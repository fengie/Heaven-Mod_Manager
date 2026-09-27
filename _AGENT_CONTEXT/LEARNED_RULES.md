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

## LR-003 — native replacement failure is not equivalent to no filesystem mutation

- **Rule ID:** LR-003
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Windows live-file replacement, rollback, and recovery tests
- **Rule:** Never model a failed native replace/rename call as proof that the destination namespace and bytes are unchanged. For `ReplaceFileW` in particular, test documented failure postconditions against actual destination/replacement bytes and recovery state before considering the boundary fail-safe.
- **Trigger / evidence:** The test-gap audit compared `AtomicFileOps.ReplaceFromAsync` with Microsoft `ReplaceFileW` documentation. Microsoft documents failure codes including `ERROR_UNABLE_TO_MOVE_REPLACEMENT` and `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2` where a false return can occur after file names, streams, or attributes have already changed. `AtomicFileOps` currently throws on false and then unconditionally attempts temp cleanup; no Windows fault regression pins those native postconditions.
- **Rationale:** Exception-based rollback is safe only if recovery reasons about the filesystem state that actually exists after the failed native operation, not an assumed all-or-nothing API contract.
- **Enforcement:** Any change near `AtomicFileOps`, `ReplaceFileW`, deployment rollback, or recovery must preserve/add Windows-only tests for native failure postconditions. Prefer a narrow injectable/native seam or controlled integration fixture; assert destination/temp/backup bytes plus journal/operation state. Do not weaken fail-closed recovery to make such tests pass.
- **Primary reference:** https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew
- **Relevant audit:** `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md`
- **Supersedes:** none
- **Superseded by:** none

