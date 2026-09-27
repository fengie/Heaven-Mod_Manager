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


## LR-005 — shareable diagnostic artifacts require export-boundary sanitization

- **Rule ID:** LR-005
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
