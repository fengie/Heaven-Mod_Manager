# Support bundle raw-log share-safety checkpoint — 2026-09-28

## Canonical baseline and task selection

- Canonical repository: `fengie/mhw-mods`.
- Canonical base inspected before editing: `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Support branch: `agent/support-bundle-share-safety-20260928`.
- The repository's nominal next production boundary was archive streaming cancellation/resource budgeting.
- A separate live checkout/branch `agent/archive-streaming-budget-20260928` claimed that boundary while this support pass was selecting work.
- Per the multi-agent rules, this checkpoint reselected a non-overlapping boundary instead of racing that agent.

Selected independent task: close the confirmed P1 raw structured-log disclosure at the support-bundle share boundary, with an end-to-end generated-ZIP regression.

## Scope

Changed:
- `src/MhwModManager.Diagnostics/SupportBundleService.cs`;
- Integration test wiring and `SupportBundlePrivacyTests.cs`;
- diagnostics/share-boundary documentation and continuity records.

Deliberately excluded:
- local/full-fidelity log production;
- Nexus credential storage;
- remote-preview network trust;
- startup/master-log manual sharing;
- database/schema/transaction ownership;
- settings allowlist/default-redact redesign;
- archive extraction and updater work.
## Confirmed pre-fix failure

A generated-bundle regression was added before the production fix. The fixture wrote a structured JSONL log containing:
- a fake `C:\Users\Alice-Privacy-Canary\...` path;
- the real fixture state-root path;
- an `Authorization: Bearer ...` canary;
- a URL `?token=...` canary;
- nested structured properties.

After the test harness gained the direct Serilog reference required to compile, the unchanged production implementation failed the focused test **1/1** because the exported ZIP still contained `Alice-Privacy-Canary`.

The earlier compile error caused by the missing test-project Serilog reference is not treated as defect evidence.

## Implementation

Support-bundle JSONL logs are no longer copied verbatim.

`SupportBundleService` now streams each selected log into the temporary bundle directory and sanitizes the share copy only. It:
- parses valid JSONL records and walks nested objects/arrays;
- redacts structured property names that look credential-sensitive;
- replaces the configured state root, current user-profile root, and temp root with placeholders;
- pseudonymizes generic Windows `<drive>:\Users\<name>` prefixes;
- redacts Authorization/Bearer text;
- redacts common secret-bearing URL query values;
- falls back to text sanitization if a line is malformed.

The original local JSONL file is never rewritten. The regression explicitly confirms the local log still contains the canaries.
## Verification performed so far

Focused red phase:
- integration test project restore/build: PASS after adding its required direct Serilog package reference;
- unchanged production + new canary test: **1/1 FAIL**, expected privacy canary present in exported ZIP.

Focused green phase:
- strict Diagnostics build with warnings as errors: **PASS**, 0 warnings / 0 errors;
- strict IntegrationTests build with warnings as errors: **PASS**, 0 warnings / 0 errors;
- support-bundle privacy test: **1/1 PASS**;
- complete Integration/fault-injection executable: **97/97 PASS**;
- `git diff --check`: PASS.

The full repository verifier/release gate has not yet been claimed at this checkpoint. Production source changed, so older green function/cache evidence does not automatically apply.

## Residuals / not closed here

This checkpoint closes only the raw-JSONL share path it reproduces.

Still separate:
- arbitrary DB telemetry/settings values need their own share-contract review;
- startup/master-log manual sharing still needs sanitizer/warning work;
- Nexus API key at-rest protection remains separate;
- remote URL/SSRF/redirect policy remains owned by the network-trust audit.

No new Learned Rule is required: LR-006 already states the durable export-boundary sanitization invariant.

## Parallel-agent handoff

Preserve the archive-streaming agent's ownership of `ArchiveInspector` and the separate updater-release-security audit. Do not merge those concerns into this branch.

Before integration, re-fetch canonical `main`, compare branch ownership again, and require a fresh exact Windows verifier/release gate for this changed production source.

The successor inherits the permanent continuity constitution and active Learned Rules, and must explicitly require its successor to preserve and recursively propagate the same system again.

**Do not break the chain.**
