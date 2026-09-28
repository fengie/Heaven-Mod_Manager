# v8.8.3 game-profile ID containment — local Windows closure

- Verified source commit: `e5eb300223c01800e3a7652ce4ffe303cbc168c5`.
- Working branch: `agent/game-profile-id-containment-v2-20260928`.
- Canonical base incorporated before verification: `86d6f9cb07fa15574aad4cc6b0c9cfd84d011c07`.
- Host: heaven2 / Windows / .NET SDK 10.0.401.
- Focused preflight: `GameProfileTests` 18/18; `MultiGameTests` 12/12.
- `scripts/Verify-Release.ps1`: 25/25 PASS.
- FunctionVerifier: 737/737 promoted; 7,863 explicit call sites; 0 uncovered; 0 trace gaps; 0 parse errors.
- Strict/relaxed solution builds and analyzers: PASS, 0 warnings / 0 errors.
- Core tests: 88/88 PASS; Automation tests: 28/28 PASS; Integration/fault injection: 184/184 PASS; self-test: 11/11 PASS.
- `scripts/Build-Release.ps1`: PASS, including win-x64 compile, ReadyToRun self-contained app publish, updater-helper publish, package verification, and fingerprint promotion.
- Updater build: 324.
- Artifact: `MHW-Manual-Mod-Manager-v8.8.3-win-x64.zip`.
- SHA-256: `75C0FD14ED8225AB37B5120243E61AA883674E969E8EED810A6257342A60DCF7`.
- An initial characterization run failed immediately because handoff metadata still declared v8.8.2 after the version bump. That failure was not claimed as verification; the manifest and CURRENT_REVISION version were repaired, `Test-AgentHandoff.ps1` passed with 88 required files, and the full gate was rerun successfully on the exact source commit above.
- Hosted exact-main verification is still pending until this branch is integrated.
