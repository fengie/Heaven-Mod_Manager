# v8.8.4 game-profile ID containment - local Windows release closure

- Exact verified source: `5d5b52a2193f8b6c377dfc9da5f504b7ad4ffc33`.
- Branch: `agent/game-profile-id-containment-v2-20260928`.
- Reconciled canonical parent: `8702934aabd6fb73bb95d29068e00169b35c2170` (v8.8.3 archive streaming failure-cleanup already integrated).
- Host: heaven2 / Windows / .NET SDK 10.0.401.
- Focused profile regressions: `GameProfileTests` 18/18 PASS; `MultiGameTests` 12/12 PASS.
- `scripts/Verify-Release.ps1`: 25/25 PASS.
- FunctionVerifier: 739/739 promoted; 7,871 explicit call sites; 0 uncovered; 0 trace gaps; 0 parse errors.
- Strict/relaxed builds and analyzers: PASS, 0 warnings / 0 errors.
- Core tests: 88/88 PASS; Automation tests: 29/29 PASS; Integration/fault injection: 187/187 PASS; self-test: 11/11 PASS.
- `scripts/Build-Release.ps1`: PASS, including win-x64 compile, ReadyToRun self-contained app publish, updater-helper publish, package verification, and fingerprint promotion.
- Updater build: 331.
- Artifact: `MHW-Manual-Mod-Manager-v8.8.4-win-x64.zip`.
- SHA-256: `62113ACCC65212B6FFDD2917FB95CFD96C058035798047541EA16DF51282E914`.
- Earlier v8.8.3 profile-containment evidence at `e5eb300223c01800e3a7652ce4ffe303cbc168c5` is historical only; it predates canonical v8.8.3 archive-cleanup integration and was not reused as proof for this combined tree.
- Exact-main hosted Windows verification remains pending until this branch is integrated.
