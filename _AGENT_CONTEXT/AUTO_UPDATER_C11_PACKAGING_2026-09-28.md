# Auto-updater C11a packaging checkpoint — 2026-09-28

## Exact source
- Working branch: `agent/auto-updater-20260928`.
- Exact committed packaging checkpoint: `2e136cc22f570a94db8da67c91aa755d4c7a3ce6`.
- Product trace-policy precursor: `a431fbbfca0f19bb77c001d0b2c96f1e64bc9589`.
- Environment: heaven / Windows x64 / .NET SDK 10.0.401.
- Test updater build identity used only for local verification: build `987654325`, source SHA exactly `2e136cc22f570a94db8da67c91aa755d4c7a3ce6`.

## What C11a now builds
- self-contained win-x64 app payload;
- self-contained multi-file updater helper invocation closure under `UpdaterHelper/`;
- `build-identity.json`;
- exact `product-files.json` with size/SHA-256 for every product-owned file;
- `release-install.json` bound to the exact build identity and product-manifest hash;
- `update-manifest.json` bound to artifact name/size/SHA-256 and product-manifest hash;
- updater ZIP verified after creation by `scripts/Test-UpdaterPackage.ps1`.
- mutable runtime debug log and protected user/runtime roots are excluded from updater ownership.

## Failed attempts preserved
1. Release verifier found three missing entry traces: `UpdateBuildIdentity.DisplayId`, `ShortSha`, and `UpdateLaunchStateStore.GetPath`. Repaired at `a431fbb...`; no cache was manually promoted.
2. Self-contained single-file helper publication failed IL3000 because shared logging uses `Assembly.Location`. C10 already supports copying a complete helper invocation closure, so C11a switched to a self-contained multi-file helper instead of broadening Core logging semantics.
3. PowerShell failed serializing a generic product-entry list with `files=@($productEntries)`. The manifest now uses `$productEntries.ToArray()`.

## Exact clean-checkout release evidence
From a freshly reset/cleaned worktree at exact `2e136cc22f570a94db8da67c91aa755d4c7a3ce6`:
- handoff continuity preflight: PASS;
- verification-cache regressions: PASS;
- FunctionVerifier scan: **727 functions**, **609 known-good / 118 requiring current verification**, **0 trace gaps**, **7746 explicit call sites / 0 uncovered**, **0 parse errors**;
- strict solution build/analyzers: PASS;
- Core: **79/79 PASS**;
- Automation: **24/24 PASS**;
- Integration/fault injection: **170/170 PASS**;
- automation self-test: **11/11 PASS**;
- App win-x64 compile/analyzers: PASS;
- App ReadyToRun self-contained publish: PASS;
- updater-helper self-contained invocation-closure publish: PASS;
- normal verifier confirm stage promoted **727/727** only inside the isolated verification worktree;
- updater package verifier: PASS;
- updater artifact SHA-256: `8A65C28FD55C7FFE7638457BE11C6AF5C9EC71A3BE2E45EF70E3261B6E077B74`.

No isolated `.verification` promotion was copied into the canonical checkout. No immutable GitHub Release publication or hosted release gate is claimed by C11a.

## Next boundary
C11b owns immutable private GitHub Release publication and publication policy:
- publish only from an exact eligible main build;
- immutable `updater-main-<build-number>` tag/release/assets;
- fail if tag/release/assets already exist;
- publish exactly the verified updater ZIP and `update-manifest.json`;
- prove stale/evidence-only/non-main cases do not create client-visible updates;
- then run hosted Windows release closure and disposable old→new plus injected-rollback end-to-end tests.

Preserve the permanent continuity constitution and active Learned Rules. The successor must recursively pass the same obligation to the agent after them.
