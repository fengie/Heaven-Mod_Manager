# Diagnostics privacy and secret-handling audit

**Support lane:** shareable diagnostics privacy, redaction, and local credential handling  
**Canonical commit inspected:** `6ada5a5c4cc83afadfba42bc6af6559540920e3d`  
**Branch:** `agent/support-8-diagnostics-privacy-audit-20260927`  
**Production source changed:** no

## Why this lane was selected

At the inspected canonical commit, parallel support work already owns Windows filesystem safety, async/cancellation/shutdown races, broad test/performance gaps, CI/supply-chain verification, MainWindow architecture, multi-source acquisition/bulk fill, and MHW semantic coverage. A state-backup/portability branch also exists.

Diagnostics privacy is independent of those boundaries and has a concrete user-facing trust boundary: the application creates support artifacts specifically so users can share them during troubleshooting. This audit therefore focuses on what crosses that share boundary and how local provider credentials are handled. It does not redesign diagnostics, filesystem safety, SQLite transactions, Nexus transport behavior, UI architecture, or the closed PlannerSnapshotRepository boundary.

## Scope

Inspected:

- `src/MhwModManager.Diagnostics/SupportBundleService.cs`
- `src/MhwModManager.Diagnostics/DiagnosticTelemetry.cs`
- `src/MhwModManager.Diagnostics/AppLogging.cs`
- `src/MhwModManager.Diagnostics/StartupDiagnosticSession.cs`
- `src/MhwModManager.Diagnostics/UnifiedDebugLog.cs`
- `src/MhwModManager.Core/MasterDebugLog.cs`
- `src/MhwModManager.Filesystem/NexusMetadataService.cs`
- `src/MhwModManager.App/App.xaml.cs`
- `src/MhwModManager.App/ViewModels/MainWindowViewModel.cs`
- `docs/DIAGNOSTICS.md`
- `scripts/Capture-Diagnostics.ps1`
- existing integration-test inventory under `tests/MhwModManager.IntegrationTests`

Questions:

1. Does the support bundle contain data that a user may reasonably not expect to share?
2. Is sensitive data scrubbed at the actual export boundary, rather than only at selected producers?
3. Can arbitrary telemetry metadata or log properties bypass redaction?
4. Are provider secrets exposed to logs/support bundles or only stored locally?
5. Are current privacy claims and user warnings aligned with actual exported content?
6. What regression contract should exist before production hardening begins?

## Methodology and evidence standard

This is a static source/control-flow audit. Findings below distinguish confirmed repository behavior from structural/theoretical risks.

No Windows runtime reproduction was performed. No support ZIP was generated in this environment. No claim is made that a real user's credentials have been exposed.

## Existing strengths

Several protections are already present and should be preserved:

- `NexusMetadataService.GetJsonAsync` sends the Nexus key in the `apikey` header but logs `apiKey=<redacted>` instead of the value.
- `SupportBundleService` does **not** copy `nexus-api-key.txt` into the ZIP.
- The support-bundle `recent-errors.json` query exports exception type/message/native code but intentionally omits the database `details` column that stores `Exception.ToString()`.
- `ExportRedactedSettingsAsync` recognizes common secret-like setting names including token, secret, password, api_key, and apikey.
- Export queries are row-bounded, and the bundle does not automatically copy mod assets, CAS blobs, or the SQLite database.
- `Capture-Diagnostics.ps1` and `docs/DIAGNOSTICS.md` already warn that traces/gcdumps can expose filenames/application state and tell users to review those artifacts before sharing.

Those controls materially reduce risk. The problems below are gaps around the remaining share path, not evidence that all diagnostics are unsafe.

## Confirmed finding P1 — support bundles copy raw structured logs across the share boundary

### Evidence

`SupportBundleService.CreateAsync` copies the five newest files from `StateRoot/Next/Logs/*.jsonl` into the support ZIP with `File.Copy`; there is no export-time transformation or redaction of those files.

Those log streams are known from source to contain environment-specific information:

- `AppLogging.Create` writes the absolute `StateRoot`.
- `App.OnStartup` records `ToolRoot`, `ModsRoot`, `StateRoot`, `GameRoot`, `Database`, and the master-log path.
- `StartupDiagnosticSession` records the application base directory and mirrors startup text to the unified log.
- Serilog events can carry rendered messages, structured properties, and exceptions; `UnifiedDebugLogSink` forwards those properties and exceptions to the master log as well.
- exception text can naturally include absolute filenames and user-specific filesystem paths.

Therefore a support ZIP intended for sharing can contain absolute local paths and other diagnostic payloads without a share-specific sanitizer.

### Impact

Absolute paths commonly reveal Windows account names, folder layout, install locations, game/library locations, and private mod organization. Raw exception/log payloads can also reveal future sensitive values added by unrelated code.

This is a **confirmed privacy-boundary defect**. It is not a confirmed credential leak: the inspected Nexus request path correctly redacts the API key.

### Recommended boundary

Do not weaken normal local diagnostics. Create a **share-safe export boundary**:

- transform structured logs when adding them to a support bundle, or omit raw logs and export an allowlisted projection;
- pseudonymize known roots such as ToolRoot, StateRoot, GameRoot, ModsRoot, user profile, and temp directories;
- preserve useful relative managed paths, event level, operation/correlation IDs, categories, timestamps, and error classes;
- scrub recognized credential/header/query-string patterns recursively from message text and structured properties.

A local full-fidelity log may remain valuable; the shareable copy should be intentionally different.

## Confirmed finding P2 — startup diagnostics explicitly contain machine/path identity and are requested for manual sharing

### Evidence

`StartupDiagnosticSession` stores:

- `Environment.MachineName`;
- `AppContext.BaseDirectory`;
- text-log path;
- exception message and full `Exception.ToString()`.

`App.xaml.cs` tells users on startup/migration failure to send the master debug log and startup diagnostic files.

### Impact

This is a deliberate diagnostic tradeoff, but it currently lacks the review warning already used by the separate hang-capture flow. A user following the application's troubleshooting instruction may share machine/path identity and full exception details.

### Recommendation

Provide either:

1. a sanitized “share startup diagnostics” export, or
2. an explicit warning that these files can contain machine name, usernames/paths, mod names, and exception payloads and should be reviewed before sharing.

Prefer routing support through the same sanitizer as support bundles rather than maintaining separate privacy logic.

## Structural finding P2 — arbitrary telemetry metadata is exported without sensitivity classification

### Evidence

`DiagnosticTelemetry.RecordSignalAsync` serializes an arbitrary `IReadOnlyDictionary<string,object?>` to `data_json`, logs that JSON, and persists it to SQLite.

`SupportBundleService` exports recent `diagnostics.data_json` values verbatim.

Current inspected `RunBusy` metadata is ordinary UI information such as title and staged-mod count. The risk is the contract: any future caller can add a path, URL, username, token, provider response, or other sensitive value and it automatically enters both local logs and shareable bundle output.

### Recommendation

Make support-export sanitization recursive and schema-independent. Treat unknown telemetry fields as untrusted input at the share boundary. Add sensitivity metadata/allowlisting where stable schemas exist, but do not depend solely on every caller remembering to classify fields.

## Structural finding P2 — settings redaction is a denylist and fails open for unknown secret names

### Evidence

`ExportRedactedSettingsAsync` exports every settings row, redacting only keys containing:

- token
- secret
- password
- api_key
- apikey

A future credential named `credential`, `authorization`, `private-key`, provider-specific shorthand, or an innocently named opaque session value would be exported.

### Recommendation

For shareable support artifacts, prefer:

- an explicit allowlist of diagnostically useful settings; or
- schema-level sensitivity classification where unknown settings default to redacted.

The existing denylist can remain a secondary defense but should not be the sole boundary.

## Confirmed finding P2 — mod/profile usage metadata is exported without a user review step

The support ZIP intentionally includes:

- managed deployment paths and provider mod IDs;
- blob/live hashes;
- conflict rules and reasons;
- profile IDs/names and enabled counts;
- incomplete operation descriptions/errors.

This data is useful for support, but it can reveal a user's installed-mod inventory, profile naming, and game-file organization.

`MainWindowViewModel.ExportSupport` generates the ZIP directly and reports its path. There is no manifest/preview or explicit privacy warning before creation/sharing.

### Recommendation

Document the contents accurately and add a concise review-before-sharing notice. A generated `CONTENTS-AND-PRIVACY.txt` inside the archive would make the artifact self-describing without requiring a complicated UI.

## Local-secret finding P2 — Nexus API key fallback is plaintext at rest

### Evidence

`NexusMetadataService.ReadApiKey` prefers environment variable `NEXUS_API_KEY`; otherwise it reads:

`<NextStateRoot>/nexus-api-key.txt`

as plaintext.

The inspected code does not copy this file into support bundles and does not log the key. This is therefore a **local at-rest protection gap**, not a confirmed exfiltration path.

### Recommendation

Treat credential storage as a separate production checkpoint from support-bundle sanitization. Evaluate Windows-native protected secret storage or another OS-appropriate protected store, with a non-destructive migration path from the existing file. Do not bundle this with Nexus transport/provider refactors.

## Theoretical risk P3 — remote image URLs can enter logs

`NexusMetadataService.CacheRemoteImageAsync` logs the full URL when a remote preview has an unsupported media type or exceeds the cache limit.

Some CDN/image URLs may contain signed or opaque query parameters. The inspected source does not prove that current Nexus image URLs contain credentials, so this remains a theoretical risk rather than a confirmed leak.

A generic URL scrubber at the support-export boundary should redact sensitive query parameter values while retaining host/path diagnostics where useful.

## Documentation mismatch

`docs/DIAGNOSTICS.md` describes the in-app support bundle as “privacy-conscious metadata.” That is only partially accurate while the bundle copies raw recent JSON logs and exports several user-specific identifiers without a share sanitizer or warning.

The same document is appropriately cautious about traces/gcdumps. The support-bundle section should adopt similarly explicit wording until a share-safe contract is implemented and tested.

## Missing regression coverage

No dedicated support-bundle privacy/redaction regression was found in the current integration-test inventory. The inspected `HardeningTests.cs`, `DebugTraceCoverageTests.cs`, and `UxHardeningTests.cs` do not reference `SupportBundleService`, support-bundle redaction, or Nexus-key canaries.

Before production hardening is called complete, add a generated-bundle canary test.

### Minimum canary matrix

Seed diagnostics/settings/log content with unique values such as:

- `C:\\Users\\Alice-Privacy-Canary`
- fake key `NEXUS_KEY_CANARY_9F...`
- `Authorization: Bearer BEARER_CANARY_...`
- `https://cdn.example/file?token=URL_TOKEN_CANARY`
- an intentionally sensitive unknown setting key;
- ordinary relative managed paths/event names that should remain useful.

Generate the ZIP, enumerate every textual entry, and assert:

1. secret/path canaries that the share contract says to scrub are absent;
2. expected placeholders are present where useful;
3. non-sensitive diagnostic facts remain present;
4. binary/database/mod payloads remain excluded;
5. unknown settings are redacted by default.

Add separate tests for structured properties and nested JSON so sanitization cannot be bypassed by moving a value from a message into telemetry metadata.

## Recommended independent checkpoints

### Checkpoint A — share-safe support export contract

Production scope:

- support-export sanitizer only;
- sanitized structured-log projection;
- settings allowlist/default-redact;
- privacy/contents manifest;
- canary integration tests.

Do **not** change the underlying local log fidelity, Nexus transport, database transaction ownership, or filesystem deployment behavior in this checkpoint.

Run a fresh exact Windows Release Gate because production diagnostics code will change.

### Checkpoint B — startup/manual-share path

Route startup/master-log sharing through the same sanitizer or provide explicit review tooling/warning. Add fixtures for machine name, user profile path, exception paths, and command-line argument redaction.

### Checkpoint C — protected provider credentials

Independently design/migrate Nexus credential storage. Preserve the environment-variable option and existing users' ability to migrate safely. Add tests that support artifacts/logs never contain the credential.

## Deliberately not changed

This support audit changes no production C#, XAML, schema, verification cache, network behavior, filesystem behavior, or credential format.

In particular it does not:

- alter normal/full-fidelity local diagnostics;
- remove diagnostically useful path information from local logs;
- move the Nexus API key;
- change provider request headers;
- modify support-bundle SQL;
- touch PlannerSnapshotRepository;
- modify any SQLite transaction owner.

## Verification actually performed

Performed:

- canonical GitHub `main` HEAD verification at `6ada5a5c4cc83afadfba42bc6af6559540920e3d`;
- recent commit/open-PR/parallel-branch inspection;
- permanent continuity/read-order inspection;
- source-body inspection of the diagnostic/export/telemetry/startup/Nexus paths listed above;
- inspection of current integration-test inventory and relevant test bodies for support-bundle/redaction coverage;
- comparison with `docs/DIAGNOSTICS.md` and `Capture-Diagnostics.ps1` privacy guidance.

Not performed:

- no Windows runtime support-bundle generation;
- no live Nexus request;
- no secret-at-rest exploit/recovery test;
- no PowerShell handoff validator execution;
- no `dotnet test`;
- no hosted Windows Release Gate;
- no verification-cache promotion.

Those omissions are intentional because this branch is documentation-only and this chat has GitHub repository access but no checked-out command execution environment.

## Parallel-agent integration notes

- PR #7 / Windows filesystem safety is the specialized authority for path containment, reparse traversal, CAS trust, and `ReplaceFileW`; this audit only treats path text as shareable metadata.
- PR #6 / async lifetime is the specialized authority for background metadata refresh and shutdown races.
- PR #3 / verification/supply-chain owns CI/release pipeline integrity.
- PR #4 / broad test/performance audit remains the general test-gap authority; this document provides the specialized privacy test contract.
- PR #5 / multi-source discovery owns future provider architecture; any provider added there must inherit the share-safe secret/logging contract described here.
- PR #8 / semantic coverage owns MHW catalog/gap-fill semantics.
- PR #9 / state backup, portability, and disaster recovery is the specialized authority for backup-set secret exclusion, WAL-safe snapshots, portable path rebasing, and recovery capsules. It opened after this audit began; its recommendation to exclude `nexus-api-key.txt` from portable backups is compatible with this audit's local-secret finding.
- PR #10 / remote-preview network trust is the specialized authority for SSRF/private-address/redirect/plain-HTTP/response-cap/image-validation risks. It opened after this audit began. This audit's URL note is narrower: even an otherwise legitimate remote URL should have sensitive query values scrubbed before it crosses a share/log-export boundary.

If another audit discovers the same issue from a broader angle, this document should remain the specialized authority for diagnostics share-boundary privacy and credential handling.

## Successor handoff

The recommended next independent action is **Checkpoint A: share-safe support export contract + canary integration tests**, as a single production verification boundary.

Before implementing it, re-check canonical `main` and open PRs because parallel support branches may have landed. Preserve the permanent continuity constitution, LR-001/LR-002 and any newly merged Learned Rules. Do not reuse LR-005's number if another branch with that rule has already merged; reconcile append-only history rather than overwriting it.

The successor must preserve this continuity system and explicitly require its successor to inherit and recursively propagate it again to the agent after them.

**Do not break the chain.**


## Late parallel-work reconciliation

Before finalizing this branch, canonical `main` was rechecked and remained at
`6ada5a5c4cc83afadfba42bc6af6559540920e3d`. Two additional documentation-only
support PRs appeared while this audit was in progress:

- PR #9: state backup / portability / disaster recovery;
- PR #10: remote preview network trust.

Neither makes this audit obsolete. PR #9 owns backup/recovery scope and PR #10
owns remote-fetch network trust. This document remains the specialized authority
for what diagnostic/support artifacts disclose when they are shared. No
production boundary is jointly owned.
