# Support-bundle share sanitization checkpoint — 2026-09-28

**Support lane:** diagnostics share-boundary privacy under LR-006
**Canonical repository:** `fengie/mhw-mods`
**Canonical main selected:** `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`
**Canonical main reconciled before verification:** `92cad66dd743c5c67e1441ad77161ccf6e531c34`
**Working branch:** `agent/support-bundle-share-sanitization-20260928`
**Reconciled implementation head:** `79345ec2b428b46e2851155838f4926d66e1ee80`
**Production source changed:** yes, diagnostics export only

## Why this task was selected

Parallel work at selection time already owned the automatic updater, Agent Control v2, frontend modernization,
archive streaming cleanup semantics, persisted game-profile path containment, launch-observation persistence,
crash-bisector controls, duplicate-cleanup recovery, profile-save atomicity, and continuity-adversarial validation.
No open PR owned diagnostics share-boundary privacy.

The existing specialized audit, `DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md`, already confirmed a P1:
`SupportBundleService.CreateAsync` copied the five newest structured JSONL logs verbatim into a user-shareable
ZIP. Those logs can contain absolute Windows paths and arbitrary structured values. LR-006 explicitly requires
centralized export-time sanitization plus path/credential canaries. Implementing that narrow checkpoint was
therefore higher value than duplicating active work or writing another research-only audit.

## Exact scope

Changed:

- `src/MhwModManager.Diagnostics/SupportBundleService.cs`
- `tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj`
- `tests/MhwModManager.IntegrationTests/SupportBundlePrivacyTests.cs`
- diagnostics documentation and this continuity record

The implementation:

1. replaces raw support-ZIP log copying with line-by-line structured JSON sanitization;
2. recursively redacts secret-like JSON property names;
3. scrubs secret-like assignments in string payloads, including bearer/token/API-key/password/secret/credential/cookie forms;
4. replaces known local roots and residual absolute Windows paths in exported log strings;
5. falls back to text sanitization if a JSONL line is malformed instead of copying it raw;
6. keeps the original local JSONL files untouched;
7. broadens the existing settings-key secret-name check and sanitizes path/assignment text in nonsecret setting values;
8. adds `CONTENTS-AND-PRIVACY.txt` to the ZIP with an explicit review-before-sharing notice;
9. adds an end-to-end generated-bundle canary regression.

## Deliberate exclusions

This checkpoint does **not** claim full LR-006 closure.

Not changed:

- local/full-fidelity logging;
- startup/master-log manual-share flows;
- Nexus API-key storage or provider transport;
- SQLite schema or transaction boundaries;
- deployment/filesystem behavior;
- arbitrary DB-backed support exports such as `recent-diagnostics.data_json`, `recent-errors.message`, or operation descriptions;
- a schema-level settings allowlist/default-redact policy.

Those remaining items are separate follow-up boundaries. In particular, the older audit's P2 finding that arbitrary
telemetry metadata can enter DB-backed support exports remains open.

## Regression contract

`SupportBundlePrivacyTests.Support_bundle_sanitizes_exported_structured_logs_without_mutating_local_log` seeds a
real recent JSONL log with:

- an absolute `C:\Users\...` privacy canary;
- a URL query `token=...` canary;
- an `Authorization: Bearer ...` structured value;
- a nested `apiKey` structured value;
- a normal relative managed path that should remain useful.

The test creates a real support ZIP and proves:

- all privacy/secret canaries remain present in the local source log;
- those canaries are absent from the exported JSONL entry;
- redaction placeholders are present;
- the ordinary relative managed path survives;
- the privacy notice is included.

## Verification actually performed

On `heaven2`, Windows with .NET SDK `10.0.401`, using an isolated detached worktree so the canonical main
checkout remained untouched:

1. The first strict Release build exposed three patch defects: invalid async disposal of `StreamReader`,
   CA1822 on the privacy-notice helper, and CA1859 on the sensitive-root return type. All were corrected.
2. The second strict build exposed two xUnit2031 analyzer errors in the new regression. Both were corrected.
3. After reconciling branch history with canonical main `92cad66dd743c5c67e1441ad77161ccf6e531c34` by a normal merge
   (no force push), the exact reconciled implementation head `79345ec2b428b46e2851155838f4926d66e1ee80` passed:
   - `dotnet build tests\MhwModManager.IntegrationTests\MhwModManager.IntegrationTests.csproj -c Release`
     — **0 warnings / 0 errors**;
   - focused `SupportBundlePrivacyTests` — **1/1 passed**.

No failed attempt is being represented as passing evidence.

## Full local Windows verification closure

After the focused test and continuity record were in place, the branch was reconciled to canonical main
`0b4e70ce27b44a4debb69702d6a38bbabe42d7c0`, producing exact source/continuity checkpoint
`1654ef63cafd280a303b82b052087e25d2b59970`.

The first repository-verifier invocation returned **23/25** because the Remote Desktop Commander shell omitted the
normal Windows `OS=Windows_NT` process marker; the report identified only the Windows-required Integration and
self-test stages as skipped. The verifier correctly refused function-cache promotion. No product/test failure was
reclassified as passing.

The tree was restored to exact checkpoint `1654ef63...` and the **unchanged verifier** was rerun with only the
standard process-local `OS=Windows_NT` marker restored. That run passed:

- repository verifier **25/25**;
- function fingerprints **735/735 verified**, **7,821 explicit call sites / 0 uncovered**, **0 trace gaps**, **0 parse errors**;
- Core **79/79**;
- Automation **24/24**;
- Integration/fault injection **178/178**;
- automation self-test **11/11**;
- strict whole-solution and per-project analyzers/builds with **0 warnings / 0 errors**;
- handoff continuity preflight, negative continuity fixtures, verification-cache regressions, and report serialization preflight.

`Build-Release.ps1` then passed on the same exact source checkpoint, including win-x64 compile/analyzers,
self-contained ReadyToRun app publish, updater-helper publish, tests, self-test, and final function confirmation.
It produced updater build **281** and ZIP SHA-256
`2CAEA5C0B9BF913D43D5F97178B0340C07C22412D0E85993FF91C0F3D1EB3ECF`.

The normal verifier-generated `.verification/function-status.json` and `.verification/stage-status.json` state
was preserved in child commit `98fbaf4e191e58efbdd88e3a6c71a493b3352048`; no manual cache promotion was performed.

Durable local evidence summary:
`_AGENT_CONTEXT/EVIDENCE/support-bundle-share-sanitization-local-windows-closure.md`.

## Not verified / intentionally still open

- No hosted Windows workflow result is claimed by this document.
- DB-backed support-export sanitization beyond the narrow changes above has not been implemented/tested.
- Startup/master-log manual-share flows and protected provider credential storage remain separate boundaries.

## Existing strengths preserved

- Full-fidelity local logs remain available for local diagnosis.
- The support bundle still omits mod assets, CAS blobs, and the SQLite database.
- Exception `details` / full `Exception.ToString()` remains excluded from the generic recent-errors query.
- Nexus request logging continues to redact the API key at the producer.
- Existing row bounds remain unchanged.

## Learned-rule decision

No new Learned Rule is warranted. This checkpoint is a direct implementation of existing **LR-006 — shareable
diagnostic artifacts require export-boundary sanitization**. Duplicating that rule would weaken the append-only
ledger's signal.

## Parallel-agent integration notes

This work deliberately avoids active updater, Agent Control, frontend, archive-cleanup, crash-bisector,
duplicate-cleanup, launch-observation, profile-save, and game-profile path-containment boundaries. The branch was
merged forward from canonical main rather than rebased or force-pushed.

The existing `DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md` remains the specialized authority for the broader
privacy threat model. This checkpoint should be treated as implementation evidence for its raw-structured-log P1,
not as a replacement for that audit.

## Recommended next independent checkpoint

After this branch is integrated and exact verification is green, independently harden the DB-backed share export
surface: recursively sanitize `recent-diagnostics.data_json`, error/operation free text, and move settings toward
an allowlist or default-redact contract. Add canaries that enumerate **every textual ZIP entry**, not only the
recent JSONL log entry.

Do not combine that follow-up with protected credential storage or provider/network redesign.

## Successor handoff

Preserve the permanent recursive continuity constitution and active Learned Rules, including LR-006 and LR-011.
Re-check canonical main and active PRs before implementation because this repository is moving concurrently.
Any successor must explicitly require its own successor to inherit, preserve, and recursively propagate the same
continuity system to the agent after them.

**Do not break the chain.**

## Programmer reconciliation and v8.8.2 release closure — 2026-09-28

While this candidate was being integrated, canonical main advanced to `9dd91767880ae6c9dcb2a31d64410c1f0bd52827`, which independently integrated the updater REST tag-verification race fix and used version 8.8.1. The privacy branch was merged forward normally; both release-note streams were preserved, and this shipped privacy boundary advanced to version **8.8.2** instead of colliding with 8.8.1.

Exact reconciled source checkpoint: `828054cc02b47684e24765c215ae5fde5135e6f1`. Release identity is synchronized across `VERSION.txt`, `Directory.Build.props`, README/CHANGELOG, startup diagnostics, structured logger identity, Nexus user agent, and continuity current-version fields.

Fresh heaven2 / Windows / .NET SDK 10.0.401 evidence on that exact source:

- updater publication policy: **PASS**;
- focused `SupportBundlePrivacyTests`: **1/1 passed**;
- `Verify-Release.ps1`: **25/25 passed**;
- FunctionVerifier: **735/735 verified**, **7,821 explicit call sites / 0 uncovered**, **0 trace gaps**, **0 parse errors**;
- Core **79/79**, Automation **24/24**, Integration/fault injection **178/178**, self-test **11/11**;
- strict per-project and whole-solution builds/analyzers: **0 warnings / 0 errors**;
- `Build-Release.ps1`: **PASS**, including win-x64 ReadyToRun app publish and self-contained updater-helper publish;
- updater package build **297**, source `828054c...`, ZIP SHA-256 `97A81911C31DD8526598A6F845BACE47D3404F5DFB4C70A3C2FF1A80B9C237FA`.

The normal verifier-generated function/stage caches are being persisted with this handoff; no manual cache promotion occurred. Hosted exact-main verification/publication is intentionally not claimed until the PR is integrated and the exact main SHA is observed.

The next independent diagnostics privacy checkpoint remains DB-backed/free-form share-boundary sanitization with canaries across every textual ZIP entry. Keep protected credential storage/provider redesign separate.

## Final pre-integration exact-source closure after hosted-evidence reconciliation

Canonical main was rechecked at `2a0acd9951d67b724a43ef79ec7078d3cc412ddc`. Its intervening changes were v8.8.1 hosted-evidence/continuity synchronization only. They were merged normally; the Windows gate evidence path/title was advanced to v8.8.2, and the privacy candidate was reverified at exact source `62670f11e640cf38b34f4722fd5e2501628bfcfb`.

Fresh heaven2 / Windows / .NET SDK 10.0.401 results for that exact source:

- updater publication policy: **PASS**;
- focused support-bundle privacy regression: **1/1 PASS**;
- `Verify-Release.ps1`: **25/25 PASS**;
- FunctionVerifier: **735/735 verified**, **7,821 explicit call sites / 0 uncovered**, **0 trace gaps**, **0 parse errors**;
- Core **79/79**, Automation **24/24**, Integration/fault injection **178/178**, self-test **11/11**;
- strict builds/analyzers: **PASS, 0 warnings / 0 errors**;
- `Build-Release.ps1`: **PASS**, including win-x64 ReadyToRun app and updater-helper publish;
- updater package build **303**, source `62670f11e640cf38b34f4722fd5e2501628bfcfb`;
- ZIP SHA-256 `228F5B9C77E090F05170B2CCA39455DF377F7970C82127566AFFD1FB875652A3`.

Any following cache/evidence-only child commit is not a new production verification target. Hosted exact-main closure still requires the actual integrated main SHA and its Windows gate/publication result.
