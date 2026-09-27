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

---

## LR-004 — lexical containment is not physical filesystem containment

- **Rule ID:** LR-004
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Windows filesystem mutation, recursive traversal, import/adoption, rollback, and recovery
- **Rule:** Do not treat `Path.GetFullPath`, `Path.GetRelativePath`, normalized managed keys, or string-prefix root checks as proof that a Windows filesystem object is physically contained by the trusted root. Before a safety-sensitive mutation or recursive import/read, explicitly account for reparse-point traversal and preserve a testable physical-containment invariant.
- **Trigger / evidence:** The Windows filesystem safety audit found that live deployment validates logical keys but performs path-based Add/Replace/Remove/rollback/recovery without the parent reparse checks already present in ArchiveInspector. It also found `SearchOption.AllDirectories` in ModScanner, unmanaged adoption, and Smart Inbox direct-directory copy; Microsoft/.NET documents that recursive enumeration includes reparse points and can loop through link cycles.
- **Rationale:** A junction or symbolic link in a parent component can keep the lexical pathname under the configured root while redirecting the actual read/write to another physical location. Byte hashes and transaction journals do not restore containment if the pathname itself resolves somewhere else.
- **Enforcement:** New or changed filesystem code must identify its trusted root, define whether reparse points are supported, add Windows tests for parent-junction escape/cycles where relevant, and keep containment validation as close as practical to the operation that depends on it. Do not weaken existing fail-closed rollback/recovery semantics to bypass a containment failure.
- **Primary references:** Microsoft reparse/symbolic-link documentation and .NET recursive-enumeration documentation cited in `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`.
- **Relevant audit:** `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`
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
- **Supersedes:** none
- **Superseded by:** none

---

## LR-006 — shareable diagnostic artifacts require export-boundary sanitization

- **Rule ID:** LR-006
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Support bundles, diagnostic exports, telemetry/log sharing, and provider secrets
- **Rule:** Treat local diagnostic/log content as untrusted when it crosses a support/share boundary. A shareable artifact must apply centralized export-time sanitization/default-redaction and must be regression-tested with path/credential canaries; producer-side redaction alone is not sufficient.
- **Trigger / evidence:** At canonical main `6ada5a5c4cc83afadfba42bc6af6559540920e3d`, `SupportBundleService.CreateAsync` copies the five newest structured JSONL logs verbatim into the support ZIP. Those logs are known from source to include absolute state/tool/game/database/log paths and can include structured properties or exception text. Nexus HTTP logging correctly redacts its API key, demonstrating that individual producers can be safe while the aggregate export boundary remains broader than intended. See `_AGENT_CONTEXT/DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md`.
- **Rationale:** Logging evolves across many call sites. Requiring every producer to anticipate every future share path is fragile; a support artifact is a distinct trust boundary and needs its own fail-safe contract.
- **Enforcement:** For any support/export path, sanitize recursively at export, default unknown settings/fields to redacted where practical, avoid copying raw logs without transformation, and add canary tests that inspect every textual archive entry for fake secrets and user-specific paths. Keep full-fidelity local diagnostics separate when they remain useful.
- **Relevant commit/run:** audit branch `agent/support-8-diagnostics-privacy-audit-20260927`, based on canonical main `6ada5a5c4cc83afadfba42bc6af6559540920e3d`; documentation-only, no runtime gate claimed.
- **Supersedes:** none
- **Superseded by:** none
