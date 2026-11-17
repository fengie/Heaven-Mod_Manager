# Inbox and whole-application performance/progress audit — 2026-11-17

## Ground truth and scope
- Initial code inspected at `fengie/Heaven-Mod_Manager@d55d09a877760220a1105a0ba1ee522846320ca6` (main, v8.8.111).
- User-visible observation: "Installing Mods from Inbox" remained on a non-informative indefinite progress bar during slow processing. No conclusion about actual throughput or deadlock can be drawn from a still frame.
- This audit is source-based. It does not claim that the current user corpus, actual installed WPF application, or an actual RAR/solid RAR5/7z operation was timed here.
- Prior Windows synthetic baseline and measured optimization attempts: [PERFORMANCE-BASELINE-2026-11-16.md](PERFORMANCE-BASELINE-2026-11-16.md), including run IDs and exact revisions. Existing [TEST-AND-PERFORMANCE-MATRIX.md](../TEST-AND-PERFORMANCE-MATRIX.md) enumerates regression and benchmark harnesses.

## Reproduced code-level faults
1. **P0 — False visual silence:** The blocking overlay had an always-indeterminate progress bar, static `BusyDetail`, no elapsed counter, no current file/count, and no human-readable operation correlation ID. Screenshot also demonstrated unreadable dark foreground on the dark dialog; the title omitted its explicit theme foreground.
2. **P0 — Work on UI thread:** `ProcessInbox` awaited `SmartInboxService.ProcessAsync` directly; that method contains synchronous directory enumeration, classifying, source moving, and filesystem scanning between awaits. WPF can lose paint/animation responsiveness during those sections.
3. **P0 — Invisible expensive phases:** `SmartInboxService` processes each package then refreshes catalog, captured hashes, Nexus metadata and categories; no progress observer was available, so a long final index/metadata phase looked like a hung importer.
4. **P1 — Missing phase durations:** Overall UI operations already use `DiagnosticTelemetry` for elapsed/cpu/alloc and have a log correlation ID. However the Inbox path only logs start/import/error per file, not *durations by phase*, making root-cause attribution hard without a separate profiler.
5. **P1 — Bulk intake contract gap:** `ImportModPathsAsync` stages files in game-specific Inboxes and does not automatically invoke SmartInbox and refresh visible rows; this is an independent behavior decision/defect, not a mere missing `ReloadMods`. Review/unsafe packages must not be silently installed.
6. **P1 — Incomplete performance scope:** Existing synthetic benchmarks include planner, family grouping, bulk ZIP routing, archive inspect/extract, hashing, metadata/classifier, SQLite reads. They do **not** cover full installed-WPF p50/p95, steady-state UI input delay, real 100s-mod RAR/7z extraction throughput, slow external storage, Inbox end-to-end indexing, duplicate cleanup, metadata network tails, Apply/Undo I/O or end-user cancellation latency.

## First implementation in PR #844
- Adds `InboxRunProgress` and `ArchiveExtractionProgress` with bounded file/byte event frequency.
- Shows stage, per-game package counts, archive file/actual-byte counts, elapsed seconds and operation ID in the WPF busy overlay; determinate bar for Inbox phases, indeterminate for unknown-duration operations.
- Runs Inbox processing and duplicate cleanup on a background worker to keep WPF dispatcher free, preserving per-item publication/rollback and user cancellation.
- Logs elapsed milliseconds for each major Inbox phase and package, plus end-to-end duration in the existing master log; does not log per-chunk payload bytes or user mod content.
- Adds two-package progress/cancel and archive byte-count regressions, plus binding/dispatcher wiring checks.
- **Not a measured throughput improvement:** avoid any positive speed claim until paired baseline/candidate measurements on the same runner.

## Cross-UI coverage and acceptance
The shared `RunBusy` wrapper governs at least 40 foreground operations across startup, analysis, apply/undo, auto-populate, archive import, intake, catalog/network browse, profiles, game discovery, storage, support, diagnosis, and managed collection review. This PR gives those operations an elapsed timer, operation ID, watchdog-compatible log heartbeat and honest indeterminate state by default. **It does not yet make every operation report a true percentage or internal substeps.**

Remaining phase-specific instrumentation should use one operation-scoped progress contract, not hard-coded fake percentages or duplicated global timers:

| Area | Measurable progress / phase checkpoints | Required validation |
| --- | --- | --- |
| Bulk folder/RAR/7z intake | inspect/identify/copy, per-source bytes, pending review count, publication | Cold/warm 8/64/256 actual archives; stalled I/O; source preservation; p50/p95 |
| Inbox import | inspect, extract, copy, classify, publish, index, hash capture, metadata | PR #844 functional regressions + installed 100/500-mod package acceptance |
| Apply/Undo/Auto Populate | snapshot, hash/capture, plan, preflight, journal, per-file commit, rollback, refresh | Lossless cancelled/failed deployment; planner equivalence; UI p95 |
| Catalog/acquisition/Nexus | per-provider requests, page and download bytes, retries/rate limits, cache write | Offline and partial-provider failure; bounded concurrency; p95 network tails |
| Startup/game switch | indexing DB/mods, binding/view load, analysis, watchers | Cold/warm p50/p95, first interactive frame and dispatcher-stall histograms |
| Search/filter/refresh | count of rows, query, database/graph updates, render cost | 100/1k/10k mods, keyboard input delay, allocations and GC |
| Storage/diagnostics | enumerated files, bytes, collected signals, cleanup/snapshot phase | Constrained disk, remote disk, cancellation and privacy/redaction |
| Auto-sorting/background | queue depth, source identity, dedup cache, failures and retry reason | Burst imports, concurrent processes and no foreground interference |

## Benchmark and logging rules
- Use `DiagnosticTelemetry` correlation IDs from JSONL/SQLite for operation latency and allocation; use `INBOX-PHASE`, `INBOX-ITEM`, and `INBOX` records in `MHW-DEBUG-ALL.log` for phase and package bottlenecks. The in-app Diagnostics tab already supports support bundle capture; validate privacy before sharing.
- Use same-runner baselines (CPU, RAM, storage, archive shape, counts, source SHA, warm/cold, filesystem cache, sample count). Report sample distribution/p50/p95 and UI blocked-frame/tick time separately from completion throughput.
- Historical 2026-11-16 baseline: 512-entry synthetic ZIP inspect ~1.76 ms vs extract/cleanup ~559 ms; synthetic 64×1 MiB ZIP bulk routing ~1.20 s; dense 500-mod planner baseline ~4.18 s/5.8 GiB managed allocation. These are not current installed-client values; subsequent planner optimizations changed those numbers.
- Preserve archive path/reparse/size limits, cancellation, dedup receipts, crash-safe publish/rollback and owner privacy. Do not speed up by skipping integrity, output validation or synchronous durability without a measured, correct alternative.
- Do not set CI wall-clock thresholds against shared GitHub runners. Functional regression checks run automatically; manual BenchmarkDotNet runs are opt-in and only count as measurement when observed with exact run IDs.
- Exact Windows Feature, Product Security, Quality, AutoUpdate and the installed WPF release acceptance remain independent requirements; no screenshot or source inspection substitutes for them.

## Outstanding gate
- Need exact-branch Windows CI evidence and installed WPF run with real user RAR/7z corpus before claiming this issue closed.
- Need per-phase tracing for the other `RunBusy` flows, an opt-in operation-latency report, and an end-to-end Inbox benchmark; track the broad UX/performance effort in [issue #735](https://github.com/fengie/Heaven-Mod_Manager/issues/735), archive acceptance in [#791](https://github.com/fengie/Heaven-Mod_Manager/issues/791) and installed release/rollback in [#793](https://github.com/fengie/Heaven-Mod_Manager/issues/793).
