# v8.8.0 repair revision — 2026-09-27

This is an updated source archive. The product version stays 8.8.0 so unrelated
build inputs and their existing verification checks do not change unnecessarily.

## Fixes

- Generic game scans retain valid `nativePC` folders instead of applying MHW-only exclusions.
- Rescanning restores a missing cached content blob from the unchanged mod source.
- Idle filesystem watchers stop writing two method-trace records every 750 ms.
- Function verification requires a real entry scope covering the body, rejects
  missing/duplicate trusted archive entries, and preserves the previous checklist
  after parse errors or duplicate function IDs.
- Integration-test cache fingerprints include the UI, scripts, and handoff files
  the tests inspect. Common build targets/configuration also invalidate caches.
- Source ZIP packaging preserves hidden verification files, validates the payload,
  and builds a replacement before overwriting an existing archive.

## Verified here

- .NET SDK 10.0.401: complete Release solution build, **0 warnings / 0 errors**.
- Core: **79/79**; Automation: **18/18**; Integration: **61/61** (18 new cases).
- Automation self-test: **11/11** checks.
- Function scan: **602 functions**, 579 checked / 23 awaiting Windows release
  confirmation; **0** parse, trace, or uncovered-call-site gaps.
- PowerShell syntax, handoff continuity, cache invalidation regressions, and ZIP
  contents/hash validation passed.

The build and tests ran on Linux using Windows targeting and the xUnit in-process
runner. This does not validate WPF interaction, Windows file locks, or a published
Windows executable. Run **Test Everything.bat** on Windows, then **Build.bat** for
the release build. Changed functions remain unchecked until the required Windows
gate confirms them. Six unchanged earlier Windows stage checks remain reusable.

The training/handoff materials and detailed evidence are in `_AGENT_CONTEXT/`.
