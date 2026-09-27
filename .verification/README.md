# Function verification state

This directory is source-development state, not runtime application data.

- `function-status.json` — live per-function checklist. Every scan rewrites the current inventory with `verified: true/false`; exact unchanged trusted/cache fingerprints are checked immediately, while changed/new functions remain unchecked until confirmation.
- `stage-status.json` — exact-input verification-stage checklist. A strict project build or test with `verified=true` may be skipped by `Verify-Release.ps1` only when its project/dependency/toolchain fingerprint is unchanged.
- `trusted-v8.7.0-files.json` — SHA-256 file hashes for the user-supplied v8.7.0 source baseline.
- `trusted-v8.7.0-src.zip` — read-only v8.7.0 production C# source used to compare individual functions when a file changed.

`MhwModManager.FunctionVerifier` scans before compilation. The scan itself persists safe known-good booleans immediately: unchanged trusted/exact-cache functions stay checked and changed/new functions are explicitly unchecked. A complete successful release still performs the stronger all-functions confirmation. Failed runs never turn a changed/new function into `verified=true`.

`stage-status.json` is intentionally more granular than release confirmation. Independent strict-build/test stages are checked as soon as that exact input fingerprint passes. If any source, transitive project reference, common build props, SDK, OS, or process architecture in the fingerprint changes, the cached check is ignored and the stage runs again. Production `Build-Release.ps1` remains a full release gate and does not rely on stage-skip caching.

The verifier now validates the trusted source zip against `trusted-v8.7.0-files.json` every scan and excludes generated `bin`/`obj` C# from the authored-source inventory. Function reports also audit explicit call-site coverage inside each inventoried body.

Latest Windows evidence (`_AGENT_CONTEXT/EVIDENCE/v8.8.0-second-windows-verification.log`) showed 602 functions, 582 already known-good and 20 changed/new. The only scan blockers were four missing entry traces; this packaged revision adds those four traces. The next scan should preserve the 582 checked fingerprints, leave the changed/current bodies unchecked until the complete gate passes, then promote all exact current fingerprints on confirmation.

## Current repair checklist — 2026-09-27

The packaged checklist now contains all 602 functions: 579 exact known-good true and 23 changed/new false. Scan gaps are zero; Linux compilation/tests passed, but Windows release promotion was intentionally not performed. Six historical Windows stage-cache entries remain reusable; FunctionVerifier changed and will miss its old hash.
