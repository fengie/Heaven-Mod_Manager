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



## Conflict/dependency proof separation (2026-09-29)

- Treat mod identity/grouping, overwrite precedence, and dependency satisfaction as separate proofs. A shared family ID, source page, or profile priority is not overwrite authority.
- For an atomic MHW structural bundle, distinct filenames can still be mutually coupled. Do not compose providers across the bundle unless a complete overlay chain or strong one-main/dependent-family relationship proves the composition.
- Any non-blocking multi-provider resolver result must name an actual candidate winner. Missing/low-confidence winner evidence is a blocker, never a reason to fall back to priority.
- Re-run hard dependency/resource/loader validation at the final normal deployment and launch boundary even when an earlier selection workflow already checked it.

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


---

## LR-007 — entity retirement must close live semantic references

- **Rule ID:** LR-007
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Durable entity deletion, mod retirement, resolver/configuration state, and identity reuse
- **Rule:** Deleting a durable entity is not complete merely because its row and declared foreign-key children disappear. Before retirement commits, classify and retire every **live semantic reference** that can affect a future entity reusing the same identity, while preserving historical evidence according to an explicit retention contract.
- **Trigger / evidence:** At canonical main `a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1`, `DuplicateCleanupService.ArchiveSafeAsync` deletes a mod row after archiving its source. FK-owned provenance/files/profile/trust/issue state cascades correctly, but planner-active `conflict_rules` and `resource_providers` have no FK to `mods`, and mod-keyed settings such as `preview:`, `visuals:`, `update:`, and `visual-public-last:` survive. `CatalogService.RefreshFoldersAsync` derives local IDs from the lowercased absolute source path, so a later different package at the same path can reuse the deleted ID and reactivate old resolver/configuration state. See `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`.
- **Rationale:** Foreign keys encode only declared ownership. Live configuration can reference an identity through untyped text columns, key/value namespaces, caches, manifests, or external indexes. Reusable identifiers turn stale references from harmless orphan data into future behavior.
- **Enforcement:** Any new deletion/retirement workflow must inventory live semantic references, distinguish them from historical records, define current-deployment preconditions, add delete -> identity-reuse regression tests, and centralize the retirement boundary instead of issuing raw entity-row deletes from feature services. Do not add blanket cascading FKs to historical or recovery-sensitive state without a migration/retention design.
- **Relevant audit:** `_AGENT_CONTEXT/MOD_LIFECYCLE_REFERENTIAL_INTEGRITY_AUDIT.md`
- **Relevant canonical base:** `a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1`
- **Supersedes:** none
- **Superseded by:** none

---

## LR-008 — import publication requires catalog-invisible staging

- **Rule ID:** LR-008
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Archive/folder import, Smart Inbox, package acquisition, catalog discovery, cancellation/restart recovery
- **Rule:** Do not treat the existence of a partially copied or extracted directory under a catalog-scanned library root as successful publication. Import work must remain catalog-invisible until validation/normalization completes, then become visible through an explicit commit-on-success publication boundary. Failure, cancellation, and process death must not allow unfinished import state to become a normal mod merely because files exist.
- **Trigger / evidence:** The import-publication audit found that `SmartInboxService` writes archive and direct-directory imports to their final `ModsRoot` destination and catches recoverable failures without removing that destination. A later global `CatalogService.RefreshFoldersAsync` enumerates every top-level `ModsRoot` directory and can persist that failed partial directory as a mod. `ArchiveImportService` uses a `.importing` staging folder, but it is also a direct child of `ModsRoot`; failure/cancellation can leave it behind and the next startup catalog refresh runs before maintenance, so the staging folder can be registered. Catalog refresh is add-only, allowing a later retry to remove the folder while leaving the stale DB row.
- **Rationale:** Cleanup is not a sufficient publication guarantee because cleanup itself can fail or be bypassed by process death. A structural separation between in-progress work and the catalog discovery namespace makes incomplete state harmless to library identity and persistence.
- **Enforcement:** New or changed import/acquisition code must stage outside the catalog-visible root (or prove an equivalently strong explicit visibility protocol), publish only after the package is complete, and add regression coverage for mixed success/failure, cancellation, restart/process-death residue, retry convergence, and exactly-once successful publication. Preserve source input until publication succeeds.
- **Relevant audit:** `_AGENT_CONTEXT/IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md`
- **Parallel numbering note:** `agent/support-mod-lifecycle-integrity-audit-20260927` independently reserved LR-007 for entity-retirement semantics. Preserve both rules; do not collapse them.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-009 — automated diagnosis must validate its control before persisting blame

- **Rule ID:** LR-009
- **Status:** Active
- **Date:** 2026-09-27
- **Scope:** Automated crash bisection, fault isolation, and persistent culprit/issue confirmation
- **Rule:** A diagnostic bisection or fault-isolation workflow must not persist high-confidence culprit state until it has proven that its current control/baseline does not reproduce the failure and that the full candidate condition does reproduce under the same relevant environment and provenance. Ambiguous, noisy, stale-provenance, or non-reproducible probe outcomes must remain inconclusive rather than becoming confirmed blame.
- **Trigger / evidence:** At canonical main `5619604e88a27176726ada8518f53d385abc7b0f`, `CrashBisectorEngine.RunAsync` begins halving immediately. `MainWindowViewModel.AutoDiagnoseCrash` does not first probe the last-known-good control or the full suspect set, while `ProbeCrashSubsetAsync` treats any process exit inside 12 seconds as reproduction. A currently-bad baseline can therefore make every tested half appear to reproduce until one arbitrary mod remains, after which `ModIssueFallbackService.MarkBisectResultAsync` persists that mod at score 99 with `confirmed=true`.
- **Rationale:** Bisection only has causal meaning when its control and positive condition are currently valid. Because confirmed diagnosis is intentionally durable and survives later normal launch success, experiment validity must be proven before confidence is persisted.
- **Enforcement:** Add explicit baseline-control and full-candidate preflight tests before narrowing; represent ambiguous/noisy outcomes as inconclusive; test stale environment/provenance and flaky probes; gate score-99/confirmed persistence on validated reproduction evidence. Reuse the canonical game-build freshness mechanism rather than creating a competing one inside diagnosis.
- **Relevant audit:** `_AGENT_CONTEXT/CRASH_BISECTOR_DIAGNOSIS_EVIDENCE_AUDIT.md`
- **Integration numbering note:** The crash support branch proposed LR-007 in parallel. Canonical integration preserves the earlier coordinated LR-007 entity-retirement and LR-008 import-publication assignments, so crash diagnosis is renumbered to LR-009 without changing its rule.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-010 — a containment check after mutation is not fail-closed

- **Rule ID:** LR-010
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** filesystem safety guards, archive/import extraction, nested directory creation, and any mutation gated by topology/authority validation
- **Rule:** When a safety check exists to prevent writes/deletes outside an allowed boundary, perform the relevant validation **before** the mutation it is meant to authorize. Throwing after a redirected create/write/delete has already occurred is detection, not containment. For nested path creation, validate existing components before descending/creating deeper components and re-check newly created components where practical.
- **Trigger / evidence:** Heavy archive stress testing reproduced a destination descendant junction where ArchiveInspector called Directory.CreateDirectory(parent) first and only then checked ancestors for reparses. Extraction threw, but external\created had already been created through the junction. The regression Archive_extraction_rejects_descendant_junction_before_creating_external_parent failed on that side effect and passed only after directory creation became component-by-component and prevalidated.
- **Rationale:** A fail-closed API must prevent the forbidden state transition, not merely report it afterward. Cleanup cannot retroactively make the original external mutation safe and may itself fail.
- **Enforcement:** In destructive/safety-sensitive code review, identify the first externally visible mutation and prove every authority/containment/precondition check required for that mutation happens before it. Add regression assertions for absence of forbidden side effects, not only for the expected exception/error result. Preserve explicit TOCTOU limitations when path-based validation cannot make the check+use atomic.
- **Relevant report:** _AGENT_CONTEXT/HEAVY_STRESS_ARCHIVE_SAFETY_REPORT_2026-09-28.md
- **Supersedes:** none
- **Superseded by:** none


---

## LR-011 — cleanup must not replace primary failure or cancellation semantics

- **Rule ID:** LR-011
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** cancellation, rollback, temp/staging cleanup, filesystem compensation, and recovery error handling
- **Rule:** When handling a primary failure or requested cancellation, best-effort cleanup must not silently replace that primary outcome with a secondary cleanup exception. Preserve cancellation/fail-closed semantics as dominant, record cleanup failure separately or attach it without reclassifying the operation, and ensure outer callers cannot mistake cleanup failure for an ordinary recoverable condition that permits continued work.
- **Trigger / evidence:** Post-PR #57 source inspection found that `ArchiveInspector.ExtractSafelyAsync` catches `OperationCanceledException`, calls `File.Delete(dest)`, and only then executes `throw;`. If that cleanup call itself throws an `IOException` or `UnauthorizedAccessException`, the original cancellation is never rethrown. `SmartInboxService.ProcessAsync` catches those filesystem exceptions as recoverable per-item failures and can continue its loop, so a cleanup failure can conditionally downgrade a user-requested cancellation into “skip this item and continue.” This control-flow result is confirmed statically; no forced-delete runtime reproduction is claimed.
- **Rationale:** Cleanup is subordinate to the operation outcome it is trying to contain. Allowing cleanup failure to overwrite cancellation or the original fail-closed error can violate caller control flow, hide the real cause, and trigger additional mutation after the user/system already requested stop.
- **Enforcement:** Fault-test cleanup paths independently from the primary failure. After ownership is established, attempt cleanup on exceptional exits, but preserve the primary exception/outcome and report secondary cleanup failure diagnostically. Callers handling broad I/O exceptions must re-check cancellation dominance before continuing. Do not use cleanup success as the only evidence that an operation is safe to resume.
- **Relevant audit:** `_AGENT_CONTEXT/ARCHIVE_STREAMING_FAILURE_CLEANUP_AUDIT_2026-09-28.md`
- **Supersedes:** none
- **Superseded by:** none
---

## LR-012 — immutable content stores require independent byte ownership

- **Rule ID:** LR-012
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** content-addressed storage, migrations, hardlinks/aliases, immutable artifact publication
- **Rule:** Do not call a content-addressed or immutable destination verified merely because its digest is correct at publication time if it still aliases bytes writable through another pathname or ownership domain. Either give the destination independent byte ownership or enforce immutability across every alias for the full lifetime of the object.
- **Trigger / evidence:** A real-Windows v7 → v8 migration probe proved `LegacyV7Migrator` can verify a hash-named CAS object and mark migration complete while the CAS pathname remains an NTFS hardlink to retained legacy bytes. Mutating the legacy pathname later changed the CAS digest; current restore integrity checks then correctly failed closed before live publication.
- **Rationale:** Point-in-time digest verification proves bytes, not ownership. A retained alias can invalidate durable verification after completion.
- **Enforcement:** For immutable/content-addressed publication, test post-publication mutation through every retained alias. Prefer independently owned verified bytes when another pathname remains mutable; otherwise explicitly enforce alias immutability.
- **Relevant audit:** `_AGENT_CONTEXT/LEGACY_MIGRATION_CAS_HARDLINK_RUNTIME_AUDIT.md`
- **Integration numbering note:** the support branch proposed LR-011, but canonical LR-011 already belongs to cleanup/cancellation semantics; this rule is renumbered to LR-012.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-013 — recovery must prove writer orphanhood before takeover

- **Rule ID:** LR-013
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** crash recovery, durable journals, multi-process mutation, startup reconciliation, filesystem/database protocols
- **Rule:** Durable incomplete state is not proof that its writer is dead. Before recovery rolls back, replays, resets, or otherwise takes ownership of an in-progress operation, it must establish exclusive mutation ownership or prove the prior writer is no longer live.
- **Trigger / evidence:** A deterministic Windows probe paused DeploymentExecutor A after it wrote MOD bytes but before its journal advanced from Writing. Live executor B recovered the same operation back to ORIGINAL; A then resumed, returned success, and committed a manifest expecting MOD. The final live-byte invariant failed twice.
- **Rationale:** Journals encode protocol state, not process liveness. SQLite transaction/busy semantics do not serialize the larger filesystem+database state machine across processes.
- **Enforcement:** High-risk recovery needs an explicit ownership model. Test live-peer exclusion, abandoned-owner takeover, independent-workspace concurrency, and abrupt-death recovery.
- **Relevant audit:** `_AGENT_CONTEXT/DEPLOYMENT_MULTI_INSTANCE_MUTATION_OWNERSHIP_AUDIT_2026-09-28.md`
- **Integration numbering note:** support branches reserved LR-011/LR-012 in parallel; canonical numbering preserves LR-011 and assigns this rule LR-013.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-014 — mutation ownership must span every supported session that can reach the target

- **Rule ID:** LR-014
- **Status:** Active
- **Date:** 2026-09-28
- **Scope:** Windows named synchronization, self-update, recovery, and single-writer mutation protocols
- **Rule:** A synchronization primitive protecting a shared mutation target must live in a namespace/identity visible to every supported process that can mutate that target. On Windows, a `Local\` named semaphore or mutex is session-scoped and cannot prove cross-session single-writer ownership of a shared installation.
- **Trigger / evidence:** The updater uses `Local\MHWMM.Update.<install-hash>` as its serialization guard. Existing regression coverage proves same-session contention only; Windows kernel-object namespace semantics make that guard distinct across interactive sessions.
- **Rationale:** Durable journals and rollback do not make concurrent writers safe. Two supported writers that acquire different lock objects can both enter code assuming exclusivity.
- **Enforcement:** Define lock scope from the mutation authority, fail closed if the required ownership primitive cannot be established, test cross-process and relevant cross-session contention, and preserve deterministic crash/abandon recovery. Define ACL/filesystem-lock behavior explicitly for cross-account shared installs.
- **Relevant audit:** `_AGENT_CONTEXT/UPDATER_CROSS_SESSION_OWNERSHIP_AUDIT.md`
- **Integration numbering note:** the support branch proposed LR-011 in parallel; canonical numbering assigns LR-014.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-015 — plugin-first routing precedes generic fallbacks

- **Rule ID:** LR-015
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** agent execution routing, reusable tooling, plugin/toolbox capability discovery
- **Rule:** Before using a generic shell, ad-hoc script, manual browser/UI sequence, broad remote-control tool, or direct API workaround, inspect and prefer the narrowest existing purpose-built plugin/capability that safely covers the task. When the ideal reusable capability is missing or materially incomplete, record an implementation-ready plan in `plugins/PLUGIN_GAP_BACKLOG.md` during the same task instead of leaving the idea only in chat.
- **Trigger / evidence:** During an Agent Control desktop-shortcut task, a manual PowerShell path was initially proposed even though the private Heaven Local Bridge plugin was the intended computer-control route. Inspecting the toolbox exposed the correct plugin and a missing first-class shortcut operation; `desktop_shortcut_create` was then implemented and merged on PR #198, and the private plugin was updated to v0.7.0. The incident showed both failure modes: incorrect routing can bypass existing tools, and useful missing operations are easy to rediscover repeatedly unless they are made durable.
- **Rationale:** Purpose-built plugins encode safety boundaries, machine ownership, validation, and reusable behavior that ad-hoc fallbacks bypass. Durable gap capture converts repeated manual work into an improving toolbox without blocking the immediate user task.
- **Enforcement:** Agent and manager prompts must perform plugin/toolbox discovery before broad fallbacks, consult the canonical plugin backlog, avoid duplicate capability plans by checking packages/branches/PRs, and add/update a backlog entry with owner boundary, API contract, security constraints, dependencies, acceptance tests, priority, and status whenever a reusable gap is found. A safe authorized fallback may still complete the current task while the future capability remains planned.
- **Relevant implementation:** PR #198 / main commit `4bf431aa4e911095a0f83b2d4e568f989118c2d4`; Heaven Local Bridge v0.7.0.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-016 — task review and current-runtime plugin activation precede routing

- **Rule ID:** LR-016
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** all agents, managers, reviewers, sub-agents, task routing, plugin/toolbox discovery, session-specific tool availability
- **Rule:** After mandatory repository training and before any task-facing plan, answer, dispatch, or task-specific action, the agent must re-read the complete assigned task, discover the actual plugin/connector/skill/toolbox surface available in the current runtime, load/read the current instructions for every materially relevant capability, and select/activate the narrowest applicable purpose-built route. A user-named plugin is a routing requirement unless current-runtime evidence proves it unavailable, unsafe, or insufficient.
- **Trigger / evidence:** During the 2026-09-29 live reliability campaign, an agent reported that the dedicated Heaven Local Bridge connector was not exposed on its chat surface and fell back to repository/GitHub coordination. The standing plugin-first policy existed, but it did not require positive current-runtime discovery, skill activation evidence, or manager rejection of unsupported “plugin unavailable” claims.
- **Rationale:** Plugin availability is session-specific and can differ across ChatGPT, local workers, managers, and spawned agents. Remembered tool lists, stale chat context, or a single failed lookup are not reliable capability discovery. Purpose-built plugins carry machine-routing, safety, authorization, and verification semantics that broad fallbacks can bypass.
- **Enforcement:** Every agent preserves a `PLUGIN-PREFLIGHT` record with the task reviewed, capabilities considered, plugins/skills loaded or activated, chosen route, unavailable/inapplicable capabilities with current-runtime evidence, and fallback reason. Managers embed the gate in dispatched prompts and reject/redispatch workers that skip it. Reviewers treat missing or false preflight evidence as a process defect when it could affect routing, safety, machine choice, or verification. Plugin activation must never bypass OAuth, user consent, repository protection, or other permission boundaries.
- **Related policy:** `AGENTS.md` mandatory task-review/plugin activation gate; `_AGENT_TRAINING/PROMPT_TEMPLATES/00_SWARM_RULES.txt`; `_AGENT_TRAINING/PROMPT_TEMPLATES/01_MANAGER_ORCHESTRATOR.txt`; LR-015.
- **Supersedes:** none
- **Superseded by:** none



---

## LR-017 — a control channel needs an independent recovery owner

- **Rule ID:** LR-017
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** persistent local agents, bridge workers, schedulers, watchdogs, operator control planes
- **Rule:** A persistent control channel is not reliable if the only mechanism that can diagnose or repair it depends on that same channel being alive. Give the channel an independent local recovery owner and a local liveness/progress signal that does not require its network/relay path.
- **Trigger / evidence:** The `heaven2` Heaven Local Bridge worker was offline while a desktop operation needed it. The existing canonical task had a finite 12-restart budget, no independent watchdog, and did not override Task Scheduler's default execution-time limit. The direct Startup worker fallback could also race the elevated scheduled worker for singleton ownership.
- **Rationale:** Restart-on-failure is finite and a worker cannot execute its own recovery when it is absent. Network-backed heartbeats do not distinguish process failure from transport failure, and a fallback that races the canonical elevated owner can degrade capability while appearing alive.
- **Enforcement:** Persistent bridge hosts require separate worker/watchdog ownership, Git-independent local process heartbeat plus queue-loop progress, indefinite Task Scheduler execution, bounded duplicate ownership, startup handoff that prefers the elevated owner, and health checks that verify the recovery path—not just process existence.
- **Relevant implementation:** PR #230, Heaven Local Bridge plugin v0.8.1.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-018 — control-path failure is not host-offline evidence

- **Rule ID:** LR-018
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** remote-control diagnosis, bridge/runner/plugin health, machine-presence claims, recovery routing
- **Rule:** Failure of a bridge, relay heartbeat, self-hosted runner, plugin surface, or remote-control action establishes failure of that control path only. Do not infer that the target host itself is offline without independent host-level evidence.
- **Trigger / evidence:** During the `heaven2` Agent Control shortcut incident, the user was actively using ChatGPT on `heaven2` while the bridge heartbeat was absent and a self-hosted workflow initially showed `runner_id=0`. The same failure class recurred on 2026-09-29 when Agent Control used `inspectHeavenBridge({ sync: false })` against a stale local relay checkout and mapped an unhealthy transport result to `not-connected`, even though the authoritative `heaven-bridge` heartbeat showed `heaven` live, elevated, and current. The recurrence proved wording-only guidance was insufficient.
- **Rationale:** A control process can be dead while the operating system, browser session, network stack, and user session remain healthy. Cached replicas can also be stale while the authoritative heartbeat is fresh. Conflating host presence, transport health, and local-cache freshness produces incorrect diagnoses and sends recovery toward the wrong target.
- **Enforcement:** Report the narrowest failed layer: bridge worker, watchdog, GitHub relay, runner allocation, plugin exposure, cache freshness, or host reachability. If local heartbeat evidence is stale/missing, perform a bounded authoritative refresh before a negative availability result where safe. Keep host-presence and transport-health states separate; an unhealthy transport with no independent host evidence is `presence-unknown`, not `offline` or `not-connected`. Dirty/stale local relay state may block mutations but must not itself make read-only presence fail. Regression tests must cover stale-cache refresh and the status mapping.
- **Relevant implementation:** PR #230 established independent bridge recovery. The 2026-09-29 recurrence is mechanically enforced by `heaven-bridge-provider.mjs` authoritative refresh logic, `bridgeMachineStatus`, Agent Control UI semantics, and provider/UI regression tests.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-019 — independent recovery owners must not share one failure domain

- **Rule ID:** LR-019
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** persistent local agents, watchdogs, task schedulers, startup recovery, operator control planes
- **Rule:** A recovery owner is not genuinely independent when it shares the same principal, trigger, scheduler lifetime, or mutable runtime dependency as the component it is supposed to repair. Critical control paths need at least one recovery owner in a different failure domain, plus a non-scheduler fallback where practical.
- **Trigger / evidence:** The first heaven2 self-healing repair added an interactive watchdog and Startup-folder fallback, but the canonical worker and watchdog still depended on the same user-session Scheduled Task subsystem. A simultaneous user-task deletion, disablement, or definition failure could strand both until another bootstrap.
- **Rationale:** Redundant processes are not redundant recovery if one configuration failure can disable them together. Separating the repair supervisor into a SYSTEM-owned machine-start task lets it restore user-scoped task definitions and the Startup fallback without relying on the bridge worker or its interactive watchdog.
- **Enforcement:** Heaven Local Bridge health requires the interactive worker, interactive watchdog, SYSTEM sentinel, Startup fallback, local heartbeat/progress signals, and runtime-copy integrity. The sentinel must never execute arbitrary bridge jobs or carry secrets; it is limited to repairing persistence and the heaven2 recovery shortcut. STOP/bootstrap handoffs quiesce the sentinel first to avoid intentional-shutdown races.
- **Relevant implementation:** Heaven Local Bridge plugin v0.8.2; `heaven-bridge/sentinel.ps1`, bootstrap/manage health enforcement, and bridge gate regressions.
- **Supersedes:** none
- **Superseded by:** none



---

## LR-020 — operator validation instances must not masquerade as installed clients

- **Rule ID:** LR-020
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** heaven2 UI validation, updater-installed clients, desktop shortcuts, operator-facing version verification
- **Rule:** A development or validation instance of a user-facing application must not remain visible on the operator desktop after its validation purpose ends, and it must never be used as proof that the updater-managed installed client is current. Verify the installed client through its canonical launcher and process path/build identity, then clean up temporary validation processes.
- **Trigger / evidence:** A v8.8.15 MHW Mod Manager validation build was left running from a HeavenBridgeSource/build-output path on heaven2 while the desktop shortcut correctly targeted the updater-managed packaged install, which was still on build 192/v8.8.14. The public build-193/v8.8.15 feed became available minutes later. The two visible windows therefore appeared to be an updated app and an old shortcut even though they were distinct installations. After the canonical install updated to build 193, launching the actual desktop shortcut was verified to start the packaged v8.8.15 executable.
- **Rationale:** Operator-visible validation windows can be mistaken for the real installed product, especially when updater publication and client polling overlap. Folder names can also remain version-stamped from the original extraction even though updater-managed contents change in place.
- **Enforcement:** Track and terminate non-installed validation app processes at task completion unless the user explicitly requests otherwise. For release/operator verification, launch the actual canonical shortcut or launcher and assert resulting executable path plus packaged build identity/version. Do not infer installed version from a parent folder name, shortcut name, or a separate validation window.
- **Related policy:** `AGENTS.md` Operator validation / installed-client identity invariant.
- **Supersedes:** none
- **Superseded by:** none

## 2026-09-29 — Every escaped bug must harden the system

- Treat every discovered bug, regression, broken integration, false completion claim, or process escape as evidence that a prevention layer was missing or insufficient.
- Do not stop at the direct fix. Record root cause and violated invariant, add/update the canonical bug precedent, strengthen the relevant guideline/process, add regression coverage when feasible, verify on the real risk surface, and propagate the lesson to future agents.
- Existing passing tests do not prove prevention was adequate when a bug escaped; add the missing test class or deterministic verifier.
- Managers and reviewers must reject bug fixes that lack applicable precedent, prevention, regression, and verification evidence.
- Repeated defects from an existing precedent mean the prior control itself failed and must be strengthened.


## LR-021 — analyzer fixes must close the sibling defect class
- **Scope:** test analyzers, cancellation-aware waits, strict/relaxed verification.
- **Rule:** A reported analyzer warning is a defect-class signal, not permission for a one-line-only repair. Inspect adjacent/sibling uses of the same API pattern and mechanically escalate the analyzer when the rule is a durable invariant.
- **Trigger / evidence:** PR #261 repaired one cancellation-unaware deployment wait, but run 36606848564 later surfaced another xUnit1051 in the same integration-test boundary.
- **Enforcement:** Integration tests treat xUnit1051 as an explicit error; bounded waits that offer a cancellation-token overload must pass the project test token. Reviewers reject a narrow analyzer repair that does not state which sibling cases were checked.

## LR-022 — primary data surfaces outrank wrapping auxiliary controls

- **Rule ID:** LR-022
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** resizable desktop UI, dense library/table pages, WPF layout
- **Rule:** When a page's main purpose is a data library/table, the star-sized primary surface gets first claim on remaining window space. Auxiliary filters, bulk actions, summaries, and help text should stay single-row where practical and use horizontal overflow, trimming, or tooltips rather than wrapping into extra vertical rows that materially shrink the primary surface at supported window sizes.
- **Trigger / evidence:** The v8.8.16 Mods-page compaction still left the windowed library feeling too small because controls around the DataGrid could occupy multiple rows.
- **Rationale:** “Responsive” wrapping is not automatically good responsiveness on dense desktop tools; it can make the core workspace progressively smaller exactly when the window is already constrained.
- **Enforcement:** Add structural/layout regressions for compact margins and overflow behavior on dense pages, and verify the actual windowed operator path when a suitable Windows UI surface is available.
- **Related policy:** `AGENTS.md` bug-prevention protocol and operator-facing verification rule.
- **Integration numbering note:** Renumbered from a concurrently assigned LR-021 during canonical reconciliation because LR-021 was already occupied by analyzer defect-class closure.
- **Supersedes:** none
- **Superseded by:** none

## LR-023 — concurrent append-only ledgers need collision-safe identity allocation

- **Rule ID:** LR-023
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** multi-agent learned-rule ledgers, incident IDs, sequential durable identifiers, integration reconciliation
- **Rule:** Multiple writers must not independently assume the same “next” sequential identifier from stale state. Allocate durable IDs under a single owner/transaction/reservation mechanism or use collision-resistant identifiers; integration must validate uniqueness and reconcile collisions before treating the ledger as canonical.
- **Trigger / evidence:** Two concurrent project changes independently created distinct active rules labeled LR-021: analyzer sibling-defect closure and dense primary-surface UI layout. The collision was discovered only during the generic-trainer promotion verification.
- **Rationale:** Append-only history is not enough when identity allocation is racy. Duplicate IDs make references ambiguous and can cause later agents to update or cite the wrong rule.
- **Enforcement:** Before assigning a sequential ID, refetch canonical state and compute/claim the next ID through the project’s designated owner mechanism. Manager/integration gates must scan for duplicate IDs. If a collision escapes, preserve both rule bodies/provenance, renumber the later/unintegrated entry, and record the reconciliation.
- **Relevant incident:** 2026-09-29 concurrent LR-021 collision found during generic trainer promotion.
- **Supersedes:** none
- **Superseded by:** none

## LR-024 — strict JSON contracts must exercise the public wire format

- **Rule ID:** LR-024
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** public JSON schemas, strict deserializers, portable manifests/recipes, DTO naming policy
- **Rule:** A strict JSON parser is not release-ready until the exact documented/public fixture is parsed with the exact production serializer options. When unmapped members are rejected or matching is case-sensitive, explicitly pin the wire naming policy (or per-member names) instead of relying on CLR property casing.
- **Trigger / evidence:** Auto Mod Recipe v1 enabled case-sensitive unmapped-member rejection but omitted the camelCase DTO naming policy. The documented `schema` property was therefore rejected as unmapped by the production parser, causing seven new Auto Modder unit tests to fail in Windows Release Gate run 36610493533 before publication.
- **Rationale:** Schema correctness and DTO correctness are separate layers. A valid public schema can still be unusable if runtime serializer naming/options disagree with the wire contract.
- **Enforcement:** Contract tests must parse at least one checked-in public fixture using production options; strict-parser changes must verify required fields, unknown-field rejection, naming/casing, enum naming, and a representative serialize/parse or parse/normalize path before integration. A release gate failure of this class requires a learned-rule update, not only a one-line serializer fix.
- **Regression:** `AutoModTests.Recipe_contract_parses_and_validates` and the checked-in `docs/examples/auto-mod/synthetic-table.recipe.json` are the initial Auto Mod Recipe v1 contract fixtures.
- **Relevant run:** Windows Release Gate 36610493533 on `6370513fda021e8f272d9c2c3e983db10b41465f`.
- **Supersedes:** none
- **Superseded by:** none



## LR-025 — generated or startup PowerShell needs parser-class regression coverage

- **Rule ID:** LR-025
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** PowerShell launchers, startup/recovery scripts, generated PowerShell, Windows automation
- **Rule:** Before a PowerShell script becomes a startup, recovery, installer, or control-plane dependency, validate it with the PowerShell parser on the target Windows surface and add a platform-independent regression for any escaped syntax class. In double-quoted strings, punctuation immediately after a variable must not create an unintended scoped-variable token; prefer `${name}:` or formatting over `$name:`.
- **Trigger / evidence:** Agent Control v0.5.6 startup restore reached heaven2 but failed parser validation because a log line contained `$taskName:`.
- **Rationale:** Structural tests can prove wiring while missing PowerShell-specific lexical rules. Startup code has a high blast radius because a syntax error can disable the recovery mechanism intended to repair everything else.
- **Enforcement:** Startup/recovery delivery gates parse every changed `.ps1` with `System.Management.Automation.Language.Parser` on Windows before registration/execution. Cross-platform source tests reject the known ambiguous variable-colon pattern so the class is caught even when PowerShell is unavailable.
- **Relevant commit/run:** failed bridge job `chatgpt-startup-setup-restore-heaven2-20260929-1819`; fixes `f78ac9337b6df935170bfcf62b726cd003d16896`, `989bebbb567cb98bffd4f41e206e09ea4c9878af`.
- **Supersedes:** none
- **Superseded by:** none


## LR-025 — structural UI regressions must move with intentional layout changes

- **Rule ID:** LR-025
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** WPF/XAML layout changes, structural UI regression tests, release verification
- **Rule:** When an intentional UI layout change replaces the structure a regression test asserts, update that regression in the same change to assert the new invariant rather than leaving a known-stale structural expectation on canonical main.
- **Trigger / evidence:** Exact UI-release verification exposed a stale structural assertion after an intentional toolbar redesign.
- **Rationale:** Structural regressions are useful only when they track intended user-facing invariants rather than obsolete implementation markup.
- **Enforcement:** Inspect and update structural UI assertions atomically with XAML/layout refactors and run the full relevant integration suite before merge.
- **Related rules:** LR-021 defect-class closure; LR-022 primary data surfaces; LR-023 collision-safe rule IDs.
- **Supersedes:** none
- **Superseded by:** none


## LR-026 — shared-provider batch launch must gate fan-out on observed startup health

- **Rule ID:** LR-026
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** multi-agent swarms, batch worker launch, shared execution providers, quota/capacity circuits, controller diagnostics
- **Rule:** A batch launcher must not treat successful process creation as successful provider startup. Between launches against a shared execution provider, re-observe the just-started worker and shared provider circuit for immediate terminal/capacity failure; stop the remaining fan-out when that shared failure appears. Persist a bounded, secret-redacted structured failure record for every terminal controller-owned failure path.
- **Trigger / evidence:** Agent Control could launch an entire swarm before the first spawned worker's asynchronous provider-capacity failure reached durable state, so every lane could fail together even though a capacity circuit existed.
- **Rationale:** Preflight checks only know the state before launch. Shared-provider failures can become knowable milliseconds later, and burst fan-out can outrun the circuit that is supposed to suppress doomed work. Without a durable failure ledger, the temporal common cause is also easy to lose after the UI/process turns over.
- **Enforcement:** Multi-step workflow tests must assert an inter-launch startup-health checkpoint after spawn and before the next spawn. Capacity/quota tests must cover the temporal race, not only already-blocked state. Failure logging must whitelist bounded diagnostic fields, redact common credentials/tokens, exclude prompt/capability/environment secrets, and expose recent records for diagnosis.
- **Relevant implementation:** `tools/agent-control/server.mjs`, `tools/agent-control/test/server-safety.test.mjs`, and the 2026-09-29 Agent Control swarm fan-out bug precedent.
- **Related rules:** provider-capacity retry suppression; LR-021 defect-class closure; LR-023 collision-safe rule IDs.
- **Supersedes:** none
- **Superseded by:** none

## 2026-09-29 — Strict analyzer gates must force recompilation

- **Trigger / evidence:** Windows Release Gate run `36656144330` on source `e3fb8fae2db8c7c6b2d41ff5ec1f510679f505e8` printed CA1865 from `NexusV3Transport.cs` during the relaxed build, then its strict Core stage reported zero warnings because MSBuild treated the just-built project as up-to-date. The subsequent fresh-checkout Updater Installed Client E2E run `36656604071` rebuilt the integration graph and failed on that same CA1865 before exercising updater behavior.
- **Violated invariant:** a strict analyzer/warnings-as-errors stage must prove diagnostics were evaluated under strict settings; an incremental no-op after a relaxed build is not evidence of that.
- **Prevention:** strict per-project verification now forces recompilation with `--no-incremental`; the verifier regression test structurally requires that flag so a future refactor cannot silently restore the false-green path.
- **Direct repair:** normalize the one-character Nexus base-URI suffix check to the char overload, closing CA1865 without behavior change.
- **Follow-up proof:** once strict recompilation was enforced, Windows Release Gate run `36658135531` correctly exposed the previously masked UnitTests warnings: one CS8629 nullable-value access and the complete eight-call xUnit1051 cancellation-token cluster in `NexusV3TransportTests`. Those sibling defects are repaired together instead of suppressing the analyzers.
- **Reusable lesson:** promoted to `_AGENT_TRAINING/VERIFICATION_DOCTRINE.md` under “Strict analyzer rerun semantics.”



## LR-027 — shared-runner CI concurrency must match the resource boundary
- **Rule ID:** LR-027
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** GitHub Actions, self-hosted runners, PR validation, release/update verification
- **Rule:** When multiple supersedable workflows contend for the same small self-hosted runner pool, use a concurrency group scoped to the shared resource/priority class rather than to each branch or PR. Keep release-critical verification in separate groups so feature churn cannot indefinitely delay publication or rollback proof.
- **Trigger / evidence:** Updater Installed Client E2E run `36659154949` was repeatedly leapfrogged by newer feature PR gates because `workflow-feature-pr-gate-${{ github.ref }}` only cancelled stale work inside each PR.
- **Rationale:** `cancel-in-progress` is only effective across runs that share a key. A per-ref key can look efficient while still flooding a shared two-runner pool across many refs.
- **Enforcement:** CI regression coverage must assert the intended concurrency key and cancellation behavior for any workflow using the shared Heaven runner labels. Any change from global to per-ref concurrency requires explicit capacity/priority justification.
- **Relevant implementation:** `.github/workflows/workflow-feature-pr-gate.yml` and `tools/agent-control/test/workflow-feature-pr-gate-concurrency.test.mjs`.
- **Related rules:** LR-021 defect-class closure; LR-026 shared-provider fan-out health gating.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-028 — provider capability declarations must constrain generated acquisition targets

- **Rule ID:** LR-028
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** Provider integrations, acquisition routing, capability/compliance contracts
- **Rule:** When a provider declares direct download unsupported or disallowed, every acquisition resolver must generate only browser-assisted/provider-authorized page targets. Never derive or expose a direct file/download endpoint merely because the provider payload contains one.
- **Trigger / evidence:** The GameBanana adapter declared `AllowsDirectDownload: false` and `BrowserAssistedDownload`, while its merged acquisition resolver constructed `https://gamebanana.com/dl/{fileId}`. The existing regression expected the provider's mod download page `/mods/download/{modId}`, exposing a contract/implementation mismatch immediately after integration.
- **Rationale:** Capability metadata is a safety and product contract, not descriptive decoration. A resolver that emits a stronger acquisition path than its declared policy silently bypasses the provider boundary even when no HTTP download is performed inside the app.
- **Enforcement:** Review provider capabilities/compliance and resolver outputs together; add a regression that asserts the exact assisted target; fail closed when identities are malformed; and never merge provider acquisition code while declared capabilities and generated targets disagree.
- **Relevant PR/fix:** PR #328 introduced the mismatch; PR #333 repairs it.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-029 — CI executable dependencies and persistent-runner PR trust must be fail-closed

- **Rule ID:** LR-029
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** GitHub Actions, self-hosted runners, dependency supply chain, release engineering
- **Rule:** Pin every non-local GitHub Action to a full commit SHA. A persistent self-hosted runner must never execute fork pull-request code. PR validation on such a runner rejects fork heads before allocation and does not persist checkout credentials. Every workflow declares explicit least-privilege permissions.
- **Trigger / evidence:** A repository audit found movable Action tags and multiple pull-request workflows targeting the persistent Heaven Windows runner. GitHub documents both movable tags and self-hosted untrusted PR execution as supply-chain/host-compromise risks.
- **Prevention:** The security supply-chain gate enforces immutable Action refs, explicit permissions, fork guards, and review of pull_request_target + checkout. Dependabot maintains pinned Action revisions.
- **Related rules:** LR-021 defect-class closure; LR-027 shared-runner concurrency.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-030 — privileged release tooling must be immutable and verified

- **Rule ID:** LR-030
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** Release engineering, CI supply chain, publication credentials
- **Rule:** A privileged release job must not execute an arbitrary preinstalled tool or dynamically select a moving latest binary. Bootstrap release tooling from a pinned immutable version/asset and verify its cryptographic digest before execution.
- **Trigger / evidence:** The Windows release gate previously trusted any `gh.exe` already on PATH, otherwise queried GitHub's moving `releases/latest` endpoint and executed the returned ZIP without an independent digest check.
- **Prevention:** GitHub CLI v2.101.0 Windows x64 is pinned to the immutable asset URL and SHA-256 `bc6c814367b193cd8e713611d61e36013c0ef843b8f516458fe3eda039192794`; the Security Supply Chain Gate rejects moving-latest/preinstalled-tool regressions and missing digest verification.
- **Related rules:** LR-021 defect-class closure; LR-029 CI executable dependency trust.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-031 — security policy scanners must not match their own rule text

- **Rule ID:** LR-031
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** Security gates, static policy scanners, CI verification
- **Rule:** Security policy checks for structured configuration must match actual structural positions (for example an anchored YAML key), not an unanchored substring that can appear inside comments, diagnostics, or the scanner's own embedded source.
- **Trigger / evidence:** The initial Security Supply Chain Gate searched for the raw substring `pull_request_target:`; because that literal appeared inside the gate's embedded Python policy, the gate falsely reported itself as a dangerous workflow.
- **Prevention:** Anchor configuration-key detection to line structure and add self-scan coverage whenever a policy scanner is embedded inside the files it scans.
- **Related rules:** LR-021 defect-class closure; LR-029 CI supply-chain trust; LR-030 privileged release tooling.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-032 — execution authentication must be end-to-end

- **Rule ID:** LR-032
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** Remote execution bridges, control planes, message authentication
- **Rule:** A worker-side authentication verifier is not a complete security control unless every authorized sender can generate the matching authenticated message without exposing key material through the transport. Hardened mode must be end-to-end and fail closed.
- **Trigger / evidence:** Heaven Bridge already enforced `HEAVEN_BRIDGE_HMAC_KEY` on the worker, but Agent Control did not sign outgoing jobs. Enabling HMAC would reject legitimate control traffic and encourage operators to leave the stronger mode disabled.
- **Prevention:** Agent Control now signs canonical bridge jobs with HMAC-SHA256 when a machine-local key is configured; sender and worker canonicalization are regression-tested, tampering changes the signature, and the key never enters Git relay payload/state.
- **Related rules:** LR-021 defect-class closure; LR-029 CI trust boundaries.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-033 — generated policy source must be syntax-safe across the generator language boundary

- **Rule ID:** LR-033
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** Code generation, CI policy scripts, cross-language automation
- **Rule:** When one language generates another language's source, replacement-string metacharacters and interpolation semantics are part of the correctness/security boundary. Never feed source containing `$`-style syntax through a replacement API that interprets replacement tokens unless a callback/literal-safe path is used.
- **Trigger / evidence:** The first generated `Test-CiSecurityPolicy.ps1` passed PowerShell regex text containing `$'` through JavaScript `String.replace` replacement-string semantics. JavaScript expanded `$'` as “text after the match,” truncating the PowerShell statement and duplicating the script tail, causing Security Supply Chain Gate run 36662225529 to fail parsing.
- **Prevention:** Use replacement callbacks or AST/literal-safe construction for cross-language source generation, run a syntax/parse check on the generated artifact before promotion, and keep the generated policy inside normal release verification.
- **Related rules:** LR-021 defect-class closure; LR-031 policy scanners must be self-safe.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-034 — agent-owned Windows consoles must be hidden by construction
- **Rule ID:** LR-034
- **Status:** Active
- **Date:** 2026-09-29
- **Scope:** Heaven Local Bridge, Agent Control, Windows process launch, startup/recovery automation
- **Rule:** On both `heaven2` and `heaven`, every agent-owned command prompt, PowerShell host, console helper, build/test child, local-agent process, and recovery child defaults to hidden/background/no-console execution and must not steal focus. A visible console requires an explicit interactive-visible-terminal request.
- **Trigger / evidence:** The operator explicitly required agent-launched command prompts and PowerShell windows to stop interrupting active desktop work. The audit showed most bridge and Agent Control launches were already hidden, but the policy was distributed and elevation/app-launch seams could still diverge.
- **Rationale:** Background automation that steals focus is an operator-safety and reliability defect even when the command succeeds. Distributed best-effort flags regress easily as new spawn sites are added.
- **Enforcement:** Agent Control runtime process creation is centralized behind a wrapper that forces `windowsHide: true` after caller options; Heaven Bridge forces `cmd.exe`, `powershell.exe`, and `pwsh.exe` to `CREATE_NO_WINDOW` even if a generic app-launch caller requests a visible console, while intentional non-shell GUI apps may remain visible; PowerShell bootstrap/elevation/recovery arguments carry `-WindowStyle Hidden`; regression tests reject direct runtime `node:child_process` bypasses and missing hidden-launch markers.
- **Related rules:** Heaven Local Bridge execution policy; mandatory bug-prevention protocol; LR-021 defect-class closure.
- **Supersedes:** none
- **Superseded by:** none

## 2026-09-29 — Inline browser scripts must be parser-validated

- **Trigger / evidence:** Agent Control's zero-agent dashboard regression came from a merge that duplicated a top-level `const federatedCount` declaration in `tools/agent-control/public/index.html`. The browser rejected the entire inline script at parse time, so initialization never ran and every metric remained at its static placeholder even while agents were active.
- **Violated invariant:** UI source that renders correctly as HTML is not deployable unless its executable JavaScript parses as one complete script after merge/integration.
- **Prevention:** Any change to inline or generated browser JavaScript must include a syntax-parse regression on the final assembled source, not only substring/DOM assertions. Duplicate declarations and other parser-fatal merge artifacts must block integration.
- **Regression:** `tools/agent-control/test/federation-dashboard.test.mjs` now parses every inline dashboard script with the JavaScript parser before integration.
- **Reusable lesson:** promote parser validation of final generated/embedded source across UI, startup, policy, and code-generation surfaces.

---

## LR-035 — active task branches require an explicit integration-ready signal

- **Rule ID:** LR-035
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Multi-agent branch cleanup, branch harvesting, integration, and verification ownership
- **Rule:** Never infer that another agent's branch is ready to merge merely because it exists, contains production changes, or appears conflict-free. Integration automation must require an explicit readiness/ownership signal plus the task's required verification evidence; branch cleanup must preserve an active owner's branch until that signal or a terminal owner state exists.
- **Trigger / evidence:** During the Mod DB RSS/Atom catalog tranche, production source commits were placed on `agent/moddb-feed-catalog-20260930-chatgpt` while regression fixtures/tests were still being authored. A concurrent cleanup/integration lane merged that active branch as PR #386 and deleted/reconciled it before the planned tests were committed, creating a source-only interval on canonical `main`. The missing tests were restored immediately and exact-SHA verification was re-queued.
- **Rationale:** Branch presence and code completeness are not equivalent to task completeness. In multi-agent repositories, a partially implemented branch may intentionally precede tests, evidence, documentation, or final owner reconciliation. Premature harvesting can turn an in-progress checkpoint into canonical state and erase the owner's isolation boundary.
- **Enforcement:** Managers/integration agents must check an explicit ready marker, PR readiness/owner handoff, or equivalent durable owner signal before harvesting another lane. Require applicable tests/gates to be attached to the candidate before merge. Cleanup agents must distinguish abandoned/terminal branches from live owned branches and must not delete or absorb the latter. If premature integration occurs, record the incident, restore missing verification immediately, and keep all evidence exact-SHA scoped. A branch is not integration-ready until its owning task explicitly signals readiness and every required gate for that exact candidate has completed successfully; queued, skipped, cancelled, or failed required gates are not merge authorization.
- **Relevant commit/PR:** PR #386; merge `559299767d26c4c20983f7e606dd8df4c786dd3a`; checkpoint `_AGENT_CONTEXT/MODDB_FEED_CATALOG_2026-09-30.md`.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-036 — multi-provider override inference requires a complete winner proof

- **Rule ID:** LR-036
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Mod conflict resolution, file-provider precedence, dependency/override planners, any N-way inferred winner selection
- **Rule:** Do not resolve three-or-more competing providers with a sequential pairwise tournament. Automatic resolution is allowed only when one candidate is proven, under the same authoritative precedence semantics, to dominate every other eligible provider. If any pair is incomparable, the relation is cyclic/non-transitive, or no unique complete dominator exists, fail closed and require an explicit choice/rule.
- **Trigger / evidence:** The MHW texture resolver compared a provisional winner to each later provider in sequence. A later winner could replace the provisional winner without ever being compared against earlier providers, so three-way non-transitive evidence could yield an order-dependent final provider even though no globally consistent winner existed.
- **Rationale:** Pairwise evidence is not automatically transitive. Deterministic iteration order only makes an unsafe tournament repeatable; it does not prove that the selected provider is compatible with all losers. Multi-provider overwrite decisions affect the final physical file tree and therefore need a global consistency proof.
- **Enforcement:** For N>=3, evaluate the required pairwise precedence relations and require exactly one provider to beat all N-1 alternatives. Invalid pair winners, incomparability, cycles, and ties are blockers. Keep dependency satisfaction, family/group membership, and overwrite precedence as separate proofs; none substitutes for another. Add cyclic/non-transitive regression fixtures whenever pairwise heuristics can disagree.
- **Regression:** `AutoCompatibilityTests.Multi_provider_texture_precedence_requires_one_complete_dominator` covers a three-provider non-transitive texture-precedence cycle and requires `ambiguous-texture-precedence` with no winner.
- **Relevant implementation:** `src/MhwModManager.Core/AutoCompatibility.cs`; PR #394; merge `b90e79acc487366f475edf31916337d57bbea59d`.
- **Related rules:** LR-021 defect-class closure; LR-023 collision-safe rule IDs; LR-035 integration-ready ownership.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-037 — integration requires canonical tree proof, not ancestry alone

- **Rule ID:** LR-037
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Multi-agent branch harvesting, branch-zero cleanup, record-only preservation merges, integration verification
- **Rule:** Never classify a task branch as integrated solely because its commits are reachable from `main`, appear as a merge base, or are parents/ancestors of a preservation merge. Verify that the intended file/content delta actually survives in the canonical `main` tree after integration.
- **Trigger / evidence:** A branch-zero preservation merge made the GitLab catalog implementation commits reachable from `main` while intentionally retaining the pre-existing main tree. Subsequent ancestry comparisons therefore looked integrated even though `GitLabReleasesCatalogPolicy.cs`, `GitLabReleasesTransport.cs`, `GitLabReleasesCatalogProvider.cs`, and their regression test were absent.
- **Rationale:** Git history can preserve provenance independently of tree state. Reachability answers “is this commit recorded in history?”; it does not answer “did this commit's content survive into the canonical product tree?”
- **Enforcement:** After every merge/fast-forward/harvest, inspect the canonical post-integration tree or an exact tree/diff invariant for the owned files and acceptance criteria. Mark record-only preservation merges as non-integrating metadata operations. Do not delete/retire a task branch until canonical tree proof and required exact-candidate verification are both recorded. Recovery logic must distinguish `ancestor-but-tree-delta-absent` from `integrated`.
- **Regression:** Agent-control/integration tests should construct a task commit, then a merge/preservation commit that records that task as ancestry while keeping the base tree; discovery must report the task as preserved-but-unintegrated.
- **Related rules:** LR-035 explicit integration-ready signal; verification exact-source semantics.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-038 — replay integration must prove replacement semantics

- **Rule ID:** LR-038
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Branch replay, cherry-pick reconstruction, file-level integration, generated/recovered source restoration
- **Rule:** When replaying or reconstructing changes onto paths that already exist, prove that the operation has replacement/patch semantics. Never concatenate a complete historical file payload with the current file merely because both represent the same path. Verify the fully assembled candidate artifact before canonical integration.
- **Trigger / evidence:** GameBanana integration PR #387 appended the older nine-file PR #379 payload in front of the already-correct current-main files. The resulting canonical tree contained duplicate C# declarations and concatenated JSON fixtures. Exact-SHA Heaven verification at `9676bd18a37873b1a7fd1128fec4549d24b0ea20` failed immediately with CS1529. Repair commit `36f5f6c136cb5927a49ae9976b50ad74e1cfd26c` restored all nine paths byte-for-byte to PR #387's pre-replay base.
- **Rationale:** File identity is not an append stream. A replay tool that treats full-file snapshots as additive text can produce syntactically invalid source, structurally invalid fixtures, or—more dangerously—valid-looking duplicated logic whose later copy silently wins. Commit ancestry cannot prove assembled-file correctness.
- **Enforcement:** Before merge, compare candidate blobs/diffs against the base and flag all-additions rewrites of already-tracked full-file paths as suspicious. Parse/build the final assembled source, parse structured fixtures, and run required tests on the exact candidate. After merge, verify canonical blob/tree content, not just ancestry. If recovery is needed, restore from a known-good exact base/replay tree rather than hand-splicing when that tree exists.
- **Regression / evidence:** `_AGENT_CONTEXT/GAMEBANANA_REPLAY_REPAIR_2026-09-30.md`; Heaven job `job-20260930T042300Z-gamebanana-repair-full-gate`.
- **Related rules:** LR-035 explicit integration readiness; LR-037 canonical tree proof.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-039 — deterministic runtime failure is not no-work

- **Rule ID:** LR-039
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Agent Control termination reconciliation, no-work recovery, swarm-tail recovery, perpetual autopilot
- **Rule:** Classify authoritative process outcome before deciding no-work recovery. A proven nonzero exit, spawn failure, or provider-capacity termination is a deterministic runtime failure and must not be auto-retried merely because terminal prose is empty or stream-loss metadata is also present. Preserve/reconcile substantive durable work if it exists; otherwise gate for diagnosis.
- **Trigger / evidence:** Perpetual workers exited with code 1, were labeled retryable no-work, then repeatedly auto-requeued until `RETRY EXHAUSTED`.
- **Rationale:** Empty output answers nothing about why a proven process failed. Treating deterministic failure as “probably never started” converts one diagnosable error into a restart storm and hides the original cause.
- **Enforcement:** No-work retry requires an unknown/lost execution outcome plus authoritative evidence that no durable work exists. Recovery planners must exclude clean deterministic failures; implementation autopilot must gate on failed ownership rather than synthesize unrelated repair work.
- **Regression:** `no-work-recovery.test.mjs` covers authoritative nonzero exit, stream-loss override resistance, clean-failure non-resurrection, and dirty-work preservation; `autopilot-core.test.mjs` covers failed implementation gating.
- **Related rules:** LR-037 canonical tree proof; bug-prevention and evidence-first recovery doctrine.
- **Supersedes:** none
- **Superseded by:** none



---

## LR-040 — durable named ownership requires atomic create-only CAS

- **Rule ID:** LR-040
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Durable plans, leases, task ownership, rollback checkpoints, idempotency keys, singleton jobs
- **Rule:** Never establish ownership with “read missing, then unconditional write.” Creation of a uniquely named durable owner must use a storage-layer create-only compare-and-swap/insert-if-absent primitive. A racing identical plan may be reconciled idempotently only after re-reading authoritative state; a different winner must remain untouched.
- **Trigger / evidence:** Rollback plan creation read a missing checkpoint and then called an unconditional upsert, allowing a competing plan with the same `change_id` to be overwritten between those operations.
- **Rationale:** Process-local sequencing cannot close a cross-process TOCTOU window. Ownership correctness belongs at the atomic persistence boundary.
- **Enforcement:** Durable stores must expose explicit create-only semantics (for this store, `expected_revision=0`); regression tests must inject a competing writer between observation and creation and prove the first durable winner cannot be replaced.
- **Relevant implementation:** `plugins/heaven-state-store/heaven_state_store/store.py`; `plugins/heaven-workflows/heaven_workflows/rollback.py`; fix `ee428f106b3cff7f3a4f570a66666a5bfb70eb61`.
- **Related rules:** LR-021 defect-class closure; LR-037 canonical-tree proof.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-041 — secret exclusion must validate credential-bearing values and sibling ingress paths

- **Rule ID:** LR-041
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Durable state, logs/artifacts, relay payloads, URL validators, secret-rejection tests
- **Rule:** A secret boundary cannot rely only on field names or one preferred adapter. Reject high-confidence credential-shaped values before persistence/relay, reject URL userinfo on every equivalent browser/network ingress path, and construct credential-rejection test canaries in a way that does not itself trip tracked-secret scanners.
- **Trigger / evidence:** Rollback state accepted a GitHub-token-shaped value under a generic key; the legacy browser accepted embedded URL credentials while the deep provider rejected them; a literal token canary blocked the repository's own tracked-secret gate.
- **Rationale:** Credentials can move through generic keys, URL authority components, legacy adapters, and test fixtures. Security invariants need boundary-wide semantic coverage rather than naming conventions.
- **Enforcement:** Add negative tests for generic-key credential values, embedded URL userinfo, and no-side-effect rejection; scan sibling adapters when introducing a security check; synthesize scanner-sensitive test canaries at runtime from safe fragments.
- **Relevant fixes:** `98c9d15a5b0aee8a34b86499c7545f41d83aa739`, `ee428f106b3cff7f3a4f570a66666a5bfb70eb61`, `2b828af3ead99192009b644a87d6e156f8535658`.
- **Related rules:** LR-021 defect-class closure; security supply-chain doctrine.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-042 — warnings-as-errors integration requires production-and-test analyzer closure

- **Rule ID:** LR-042
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** C# strict builds, catalog/provider integration, test projects, release verification
- **Rule:** When warnings are errors, integration readiness requires the affected production project and its affected test project to compile under the repository's pinned analyzer profile. If a build emits downstream missing-assembly/test-artifact errors, diagnose the earliest compiler/analyzer diagnostics first; do not treat cascade failures as separate defects.
- **Trigger / evidence:** Windows Release Gate run `36669017493` failed on six Core analyzer diagnostics and then cascaded into missing artifacts. After those six were fixed, exact Heaven verification of `cde589f2330f3e10e92b860ffe16dcdb4fc0d9c2` produced a clean Core build (0 warnings / 0 errors) but then exposed four additional analyzer failures while compiling `MhwModManager.Tests`.
- **Rationale:** A production-only green compile is insufficient when the test assembly itself participates in strict analyzer enforcement. Cascade diagnostics obscure the causal boundary and waste debugging effort if treated independently.
- **Enforcement:** For Core/catalog changes, run the strict Core build plus affected test-project compile/tests or the full exact-candidate repository verifier before signaling integration-ready. Preserve public/API shape when an analyzer-only refactor would introduce needless external churn; use a narrowly justified suppression only when the design intentionally requires the shape.
- **Regression/evidence:** production fix lineage through `cde589f2330f3e10e92b860ffe16dcdb4fc0d9c2`; Heaven job `job-20260930T044200Z-bugfix-core-analyzers`; test repair PR #437.
- **Related rules:** LR-002 compile-backed caller closure; exact-SHA verification doctrine; generic trainer lesson 33 on strict analyzer closure.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-043 — resource-owning open/create operations must be transactional

- **Rule ID:** LR-043
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Browser/session/tab creation, host resources, multi-index registries, connection/session factories
- **Rule:** When an operation acquires host resources or registers identity before a later fallible step, success must be all-or-nothing. On failure, undo every newly acquired resource and every registry/index/active-pointer mutation. Use one authoritative removal helper for objects represented in multiple indexes.
- **Trigger / evidence:** Deep-browser initial navigation could fail after browser/context/session creation and leak the owned session; failed new-tab navigation and externally closed tabs could leave stale tab identity/active selection behind.
- **Rationale:** Partial construction turns a single recoverable failure into durable leaked resources and poisoned future state. Duplicated cleanup paths drift and repair only part of the state graph.
- **Enforcement:** Failure-path tests must inject an exception after registration and assert resource closure plus complete registry restoration. Public enumeration/pruning paths must use the same cleanup primitive as explicit close. Structured URL/status renderers must preserve authority syntax while redacting sensitive components.
- **Regression/evidence:** `04e706ba2a8b7e0de5ab0461af3702b9db854a33`, `339b4caf7fdd00eb0aa6d7b29c3149a9537d9f71`, `db27ed786337529fa5674ff735c5d06038a9270f`, merged via PR #438 as `b9b3cd9ec0c76c44556bf3f1e3014f2d5a122620`.
- **Related rules:** LR-021 defect-class closure; LR-037 canonical-tree proof; LR-041 boundary-wide URL/secret validation.
- **Supersedes:** none
- **Superseded by:** none

## 2026-09-30 — Client-visible updater feed must lead canonical release visibility

## 2026-09-30 — Stale integration can revert newer safeguards without an explicit revert

---

## LR-044 — copied regression logic must be reconciled to the destination test module

- **Rule ID:** LR-044
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Test refactors, copied regression snippets, cross-file test reuse, security regression additions
- **Rule:** Treat copied test code like copied production code: reconcile imports, aliases, fixtures, helpers, and execution conventions to the destination file, then execute that exact test file before integration. Never assume a snippet is valid because equivalent logic passed in a sibling test module.
- **Trigger / evidence:** HMAC-rotation regressions copied the `hb.*` module alias into `heaven-bridge/tests/test_worker.py`, whose canonical alias is `worker`; exact verification failed with `NameError` before behavioral assertions ran.
- **Rationale:** A regression test that cannot execute provides zero defect-prevention value and can falsely make a change appear covered during source review.
- **Enforcement:** For copied/moved test logic, inspect the destination imports and helpers, run the exact affected test file, and treat test-runtime/name-resolution failures as completion blockers. Record the failure as a precedent when it reaches an integration candidate.
- **Regression/evidence:** failing job `chatgpt-20260930-045100-hmac-rotation-exact-verify`; fixed commit `b0b4bf2300efe347aada7e4b5e699cb4b89188c6`; passing rerun `chatgpt-20260930-045500-hmac-rotation-main-rerun` (19 Python + 17 Node tests).
- **Related rules:** LR-002 compile-backed caller closure; LR-021 defect-class closure; exact-SHA verification doctrine.
- **Supersedes:** none
- **Superseded by:** none

## LR-045 — control-plane polling must preserve operator intent

- **Rule ID:** LR-045
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Agent/control dashboards, polling UIs, lifecycle controls, asynchronous status refresh
- **Rule:** Background status polling must never overwrite newer authoritative responses or erase operator-selected control values. UI lifecycle actions must derive from the same authoritative active/terminal state model as the server rather than a smaller hand-maintained subset.
- **Trigger / evidence:** Agent Control polled the full snapshot every four seconds without sequencing requests, rebuilt the worker-target selector on every render (resetting the operator's chosen machine), and exposed Stop only for running/starting/stopping even though reserved/waiting/blocked/stale are server-defined active managed states.
- **Rationale:** A control plane can be backend-correct yet operationally unreliable when stale asynchronous responses or render-time defaults undo the human's latest intent. Divergent lifecycle enums also strand live owned work behind the wrong action.
- **Enforcement:** Serialize ordinary polling, sequence-check any concurrent authoritative refresh/sync responses, preserve user-controlled selection across renders, and centralize or regression-pin lifecycle action coverage against server-defined states.
- **Regression/evidence:** Agent Control v0.5.10 dashboard change set beginning at `3ab57f1da041171e043a65078d3294335bcee97c`; `federation-dashboard.test.mjs` pins active-state Stop coverage, selection preservation, and stale-response rejection markers.
- **Related rules:** LR-039 deterministic runtime failure classification; operator-control and liveness doctrine.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-046 — persistent supervisor fixes require installed-runtime and restart proof

- **Rule ID:** LR-046
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Windows scheduled tasks, watchdogs/sentinels, startup recovery, background agent services, hidden-console guarantees
- **Rule:** Do not treat a corrected source file or scheduled-task definition as proof that a persistent supervisor is fixed. Verify the **installed runtime copy** that actually owns reconciliation, then perform a controlled restart and inspect the relaunched process/task postconditions. Self-healing code must be treated as an active writer that can revert manual repairs when its installed runtime is stale.
- **Trigger / evidence:** On `heaven`, canonical bridge source already required `-WindowStyle Hidden`, but the installed `.mhw-local-tools\heaven-bridge-sentinel.ps1` was stale and re-registered both Sentinel and Watchdog without the hidden flag. A manual task-action repair initially succeeded, then a restart caused the stale Sentinel to overwrite it; the Watchdog relaunched with a visible PowerShell window. After synchronizing the canonical Sentinel/Watchdog runtime files, reapplying the hidden task actions, and restarting both supervisors, the live processes reported `MainWindowHandle = 0` and both task actions retained `-WindowStyle Hidden`.
- **Rationale:** Persistent recovery owners are not passive configuration. Source/runtime drift lets old reconciliation logic defeat otherwise-correct repairs, so source inspection and pre-restart task state can produce false confidence.
- **Enforcement:** For persistent-agent/startup fixes, identify every reconciliation owner, compare or synchronize installed runtime artifacts with the intended canonical source, apply configuration changes, restart the actual owner, then verify live process/window/task state. Regression policy must cover both the desired launcher arguments and the deployment/reconciliation path that preserves them.
- **Relevant commits/evidence:** runner hardening `118ca31d9a272232506930589c6e317208e9cc0a`; regression `1b2cfe7ba110d54f6c4531ac5c458272405c7daf`; live bridge verification job `chatgpt-20260930-0458-finalize-hidden-supervisors`.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-047 — operator controls must close over authoritative server actions

- **Rule ID:** LR-047
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Agent/control dashboards, recommendation engines, safety latches, state-dependent operator actions
- **Rule:** If the server computes an operator recommendation or rejects an action for specific lifecycle states, the UI must reflect that same contract. Do not hide actionable authoritative recommendations, advertise mutations that are invalid for the current state, or label a recovery control as successful when a safety latch remains set.
- **Trigger / evidence:** Agent Control exposed `suggestedActions` in snapshots but did not render them; terminal/quota-blocked agents could inherit a Deploy reviewer button despite server refusal; and Resume always sent `clearEmergencyStop:false`.
- **Rationale:** A backend-correct control plane is still functionally broken when the operator cannot reach valid recovery/review actions or is offered known-invalid actions. Safety recovery must be explicit rather than silently partial.
- **Enforcement:** Regression-pin the mapping from server action/state to UI affordance; state-dependent safety-latch clearing requires explicit operator intent/confirmation; unsupported recommendation types stay informational rather than being guessed into mutations.
- **Regression/evidence:** v8.8.24 operator-actions integration PR #452; `tools/agent-control/test/operator-ui-cli.test.mjs`; canonical assembled inline JavaScript parse proof.
- **Related rules:** LR-017 independent recovery owner; LR-039 deterministic failure classification; LR-045 operator-intent-preserving polling.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-048 — generated operator surfaces and mirrored manifests require whole-output closure

- **Rule ID:** LR-048
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Generated HTML/operator controls, inline handlers, plugin/package manifests, mirrored release metadata
- **Rule:** Treat generated executable markup and mirrored release metadata as whole outputs. Never nest raw serialization inside an already quoted handler/attribute; cross an explicit encoding or DOM-binding boundary. When one component has multiple distributable manifests, regression-check every manifest against the authoritative runtime release identity.
- **Trigger / evidence:** Agent Control's Copy branch action nested a JSON-quoted branch value inside a double-quoted onclick attribute, while the nested Codex plugin manifest remained v0.6.1 after runtime/root plugin advanced to v0.6.3.
- **Rationale:** Template source can look locally valid while the emitted markup is structurally invalid, and partial version checks permit stale package identities to ship unnoticed.
- **Enforcement:** Add emitted-output/source-pattern regressions for generated controls and maintain an explicit identity set for every package/plugin manifest that represents the same component.
- **Regression/evidence:** v8.8.25 Agent Control repair; `tools/agent-control/test/operator-ui-cli.test.mjs`; `tools/agent-control/test/agent-manager-priority.test.mjs`.
- **Related rules:** LR-021 defect-class closure; LR-045 operator-intent-preserving polling; LR-047 authoritative operator-action closure.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-049 — parser contracts must include producer-specific text encoding

- **Rule ID:** LR-049
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Cross-platform JSON/text automation, PowerShell-generated files, manifests, installer/updater metadata
- **Rule:** When one tool/runtime produces text that another runtime parses, test the producer's real encoding behavior. Windows PowerShell 5.1 UTF-8 output may include a BOM; consumers that promise UTF-8 compatibility should accept BOM and BOM-free forms unless the format explicitly forbids one.
- **Trigger / evidence:** The plugin version pruner passed Python unit fixtures but skipped live PowerShell-written JSON manifests because plain `utf-8` decoding exposed the BOM to `json.loads`.
- **Rationale:** Encoding is part of the serialization contract. Synthetic fixtures that use a different writer can create false confidence even when schema/content tests are otherwise complete.
- **Enforcement:** Add producer-realistic fixtures or a live producer/consumer smoke for cross-runtime file boundaries; prefer `utf-8-sig` for JSON/text readers that must accept both common Windows UTF-8 forms; keep malformed-content handling fail-closed.
- **Regression/evidence:** `test_windows_utf8_bom_manifests_are_supported`; failing live job `chatgpt-20260930-verify-plugin-pruner-heaven2-a1`; fix `ccff83a93c39233a0231456ac60b919cf003d8f9`.
- **Related rules:** LR-044 copied-test destination validation; LR-046 installed-runtime/restart proof.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-050 — generated persistent artifacts require consumer-side proof

- **Rule ID:** LR-050
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Generated scripts, persisted JSON/status files, startup fallbacks, cross-language automation
- **Rule:** Validate generated and persisted artifacts with the interpreter/parser that will actually consume them. Producer success, source-language parsing, or visually plausible output is insufficient.
- **Trigger / evidence:** Plugin-pruner installation succeeded while its JSON audit ended in a literal `\\n` and its generated Startup VBS over-escaped the `CreateObject` call; both defects survived producer-side checks.
- **Rationale:** Cross-language boundaries can corrupt otherwise-correct values through escaping/serialization mistakes that neither producer parser nor core behavior tests detect.
- **Enforcement:** For persisted JSON, parse the exact written file in regression/live verification. For generated scripts/configuration, parse or execute the exact generated artifact in its target runtime before declaring persistence healthy.
- **Regression/evidence:** plugin-pruner log round-trip test, WScript quoting regression, and live reinstall validation on both Heaven machines.
- **Related rules:** LR-046 installed-runtime/restart proof; LR-049 producer-specific text encoding.
- **Supersedes:** none
- **Superseded by:** none


---

## LR-051 — provider preflight and execution must share configuration resolution

- **Rule ID:** LR-051
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Provider relays, service discovery, runtime configuration, health checks, job submission, result polling
- **Rule:** Any documented default or fallback accepted by provider health/preflight must be resolved through the same authoritative function when real execution submits work and waits for results. Do not validate one path and then re-read only one environment variable at execution time.
- **Trigger / evidence:** Heaven Bridge health could discover `~/HeavenBridgeRepo`, but submit/result-wait bypassed that resolver and threw when `AGENT_CONTROL_HEAVEN_RELAY_DIR` was absent, deterministically failing both observed main and manager workers with exit code 1.
- **Rationale:** Divergent configuration semantics create false-green health followed by guaranteed runtime failure; retry/recovery layers then amplify the original configuration bug into misleading lifecycle noise.
- **Enforcement:** Centralize provider path/config resolution, regression-test the documented-default/no-explicit-env execution path, and diagnose the first authoritative runtime error before tuning retry policy.
- **Regression/evidence:** v8.8.27; `tools/agent-control/lib/heaven-bridge-provider.mjs`; `tools/agent-control/test/heaven-bridge-provider.test.mjs`; worker evidence `main-20260930042701-ldewu` and `manager-20260930042717-kmbi6`.
- **Related rules:** LR-039 deterministic failure classification; LR-046 installed-runtime/restart proof; v8.8.26 retry-exhausted registry retirement.
- **Supersedes:** none
- **Superseded by:** none

---

## LR-052 — remote retirement requires monotonic proof across execution layers

- **Rule ID:** LR-052
- **Status:** Active
- **Date:** 2026-09-30
- **Scope:** Remote workers, relay wrappers, live registries, retirement tombstones, heartbeat/session replay
- **Rule:** A remote-backed entity may leave live registry state only after every execution layer is authoritatively terminal. Local wrapper exit, `not_running`, `unknown`, or any generic non-running state is insufficient. Re-registering a retired provider source requires raw lifecycle evidence strictly newer than the retirement tombstone before normalization can synthesize freshness.
- **Trigger / evidence:** Heaven Bridge cancellation can report `not_running` for queued/unclaimed work while `job_status` reports `unknown` outside RUNNING/PROCESSED; stale live-state provider payloads could also clear tombstones without proving a newer heartbeat.
- **Rationale:** Multi-hop systems can outlive their local wrapper and stale replicas can look live. Absence of running evidence is not terminal proof, and normalized freshness is not new-session proof.
- **Enforcement:** Whitelist explicit processed terminal states, fail closed for every ambiguous/authority-failure state, re-cancel running races, and compare raw heartbeat/session evidence monotonically against retirement time before clearing tombstones.
- **Regression/evidence:** v8.8.31 `registry-retirement.test.mjs` terminal/queued/race/authority matrix and retired-heartbeat matrix; real heaven2→heaven1 smoke remains required.
- **Related rules:** LR-017 independent recovery ownership; LR-039 deterministic failure classification; LR-051 provider resolution parity.
- **Supersedes:** none
- **Superseded by:** none
