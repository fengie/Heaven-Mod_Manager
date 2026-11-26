# Function verification state

This directory is source-development state, not runtime application data.

- `function-status.json` — live per-function checklist. Every scan rewrites the current inventory with `verified: true/false`; exact unchanged trusted/cache fingerprints are checked immediately, while changed/new functions remain unchecked until confirmation.
- `stage-status.json` — exact-input verification-stage checklist. A local strict project build or test with `verified=true` may be reused by `Verify-Release.ps1` only when its exact inputs, semantic invocation and verifier policy match; CI always executes fresh stages.
- `trusted-v8.7.0-files.json` — SHA-256 file hashes for the user-supplied v8.7.0 source baseline.
- `trusted-v8.7.0-src.zip` — read-only v8.7.0 production C# source used to compare individual functions when a file changed.

`MhwModManager.FunctionVerifier` scans before compilation. The scan itself persists safe known-good booleans immediately: unchanged trusted/exact-cache functions stay checked and changed/new functions are explicitly unchecked. A complete successful release still performs the stronger all-functions confirmation. Failed runs never turn a changed/new function into `verified=true`.

`stage-status.json` is a local development hint, not authoritative release evidence. Stage fingerprint v2 binds project/transitive source, common build files, SDK/platform/architecture, the semantic command argument vector, and the verifier's own bytes. Integration tests additionally bind reviewed control inventories, feature matrices, repository contracts, workflow definitions, test fixtures, data, and continuity sources. Generated `bin`/`obj`, stage/function cache state, and the per-run diagnostic binlog destination are excluded; these are outputs, not proof. Historical v1 fingerprint values cannot match the new keys. Duplicate stage identities fail closed, non-boolean verification cannot be credited, and an observed stage failure invalidates its previous positive cache record. `CI=true` or `GITHUB_ACTIONS=true` prevents historical reuse. Production `Build-Release.ps1` remains a full release gate.

Restore, relaxed dependency compilation, all strict project checks, and strict solution compilation must succeed before any `--no-build` test stage is eligible. Independent strict diagnostics continue after a project failure where the dependency build succeeded. Blocked dependent stages are explicit failures with exit code 9002, not passing or skipped evidence; function-cache promotion still requires the entire verifier to pass. Verification-harness and reviewed-inventory edits trigger the existing feature gate.

The verifier now validates the trusted source zip against `trusted-v8.7.0-files.json` every scan and excludes generated `bin`/`obj` C# from the authored-source inventory. Function reports also audit explicit call-site coverage inside each inventoried body.

Latest Windows evidence (`_AGENT_CONTEXT/EVIDENCE/v8.8.0-second-windows-verification.log`) showed 602 functions, 582 already known-good and 20 changed/new. The only scan blockers were four missing entry traces; this packaged revision adds those four traces. The next scan should preserve the 582 checked fingerprints, leave the changed/current bodies unchecked until the complete gate passes, then promote all exact current fingerprints on confirmation.

## Current repair checklist — 2026-09-27

The packaged checklist now contains all 602 functions: 579 exact known-good true and 23 changed/new false. Scan gaps are zero; Linux compilation/tests passed, but Windows release promotion was intentionally not performed. The historical Windows stage-cache entries are preserved as evidence; later stage-key revisions invalidate their reuse.
