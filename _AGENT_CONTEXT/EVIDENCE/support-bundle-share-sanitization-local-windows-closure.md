# Support-bundle share sanitization — local Windows closure

Date: 2026-09-28  
Branch: `agent/support-bundle-share-sanitization-20260928`  
Exact verified source/continuity checkpoint: `1654ef63cafd280a303b82b052087e25d2b59970`  
Verifier-generated cache persistence child: `98fbaf4e191e58efbdd88e3a6c71a493b3352048`  
Machine: `heaven2` / Windows  
.NET SDK: `10.0.401`

## Scope verified

This evidence applies to the support-bundle recent-JSONL share-boundary sanitizer, its generated-bundle privacy
canary regression, privacy notice/documentation, and continuity registration present at exact source checkpoint
`1654ef63...`.

## Focused evidence

After the branch was merged forward from canonical main:

- strict Release build of `MhwModManager.IntegrationTests.csproj`: **0 warnings / 0 errors**;
- `SupportBundlePrivacyTests`: **1/1 passed**.

The regression creates a real support ZIP containing a privacy-canary log and proves secret/path canaries are
removed from the exported JSONL while remaining untouched in the full-fidelity local log.

## Repository verifier

Initial invocation: **23/25**, with only:

- Integration + fault injection — skipped because the remote shell lacked the Windows `OS` marker;
- Full automation self-test — skipped for the same reason.

The verifier explicitly preserved the prior function cache instead of promoting through that incomplete run.

Before retry, tracked verification/debug files were restored to exact branch state. The unchanged verifier was
rerun with process-local `OS=Windows_NT`, matching the normal Windows environment.

Final verifier result:

- **25/25 passed**;
- function fingerprints: **735/735 verified**;
- explicit call sites: **7,821 / 0 uncovered**;
- trace gaps: **0**;
- parse errors: **0**;
- Core: **79/79**;
- Automation: **24/24**;
- Integration/fault injection: **178/178**;
- automation self-test: **11/11**;
- relaxed and strict whole-solution builds: PASS;
- strict per-project analyzers: PASS;
- PowerShell syntax, handoff continuity, negative continuity fixtures, verification-cache regressions, and report serialization: PASS.

## Release gate

`Build-Release.ps1` passed on exact checkpoint `1654ef63...`.

Passed stages included:

- solution restore and analyzers;
- Core **79/79**;
- Automation **24/24**;
- Integration/fault injection **178/178**;
- automation self-test **11/11**;
- App win-x64 compile/analyzers;
- win-x64 self-contained ReadyToRun publish;
- updater-helper win-x64 self-contained publish;
- final function fingerprint confirmation (**735 known-good / 0 needing verification**).

Produced updater package metadata:

- updater build: **281**;
- source: `1654ef63cafd280a303b82b052087e25d2b59970`;
- artifact: `MHW-Manual-Mod-Manager-v8.8.0-win-x64.zip`;
- SHA-256: `2CAEA5C0B9BF913D43D5F97178B0340C07C22412D0E85993FF91C0F3D1EB3ECF`.

## Verification-state handling

Only the normal verifier/build pipeline promoted function/stage verification state. The resulting tracked
`.verification/function-status.json` and `.verification/stage-status.json` changes were committed as
`98fbaf4e191e58efbdd88e3a6c71a493b3352048`. `MHW-DEBUG-ALL.log` and transient `BuildLogs/` were not committed.

## Upstream reconciliation

Immediately after the release run, canonical main had advanced from the merged base `0b4e70c...` to
`79a5be1bb71f1a695b9a838a87c4fbdb92baf211`. The four intervening commits changed only `AGENTS.md`,
`README.md`, and an Agent Control v2 safety audit; they did not touch diagnostics source, the privacy test,
diagnostics docs, or the support-bundle handoff. That movement was therefore recorded rather than merged into the
already exact-verified production checkpoint.

## Not claimed

- No hosted Windows workflow pass is claimed here.
- No full LR-006 closure is claimed; DB-backed free-form support entries and startup/manual-share paths remain open.

Preserve this evidence with the specialized audit and require the successor to preserve and recursively propagate
the continuity system to the agent after them.
