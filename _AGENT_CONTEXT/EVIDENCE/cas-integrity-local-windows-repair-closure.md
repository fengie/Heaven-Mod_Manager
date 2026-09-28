# CAS integrity fresh local Windows repair closure

Date: 2026-09-27 (America/New_York)
Runner: heaven2
Repository: fengie/mhw-mods
Exact source tested: 3556bddcd7c7f84c0efe2ff92f6d73e12842128f
Production CAS repair hosted reference: d001870d4cd3549841d8511392ae7885f174bca2 / Windows Release Gate 36367883836

## Focused CAS test

Command:
`dotnet run --project tests/MhwModManager.IntegrationTests -c Release -- -class '*BlobIntegrityTests'`

Result: PASS — 10 total, 0 errors, 0 failed, 0 skipped.

## Full repository verifier

Initial unchanged `scripts/Verify-Release.ps1` run: 23/25 because Remote Desktop Commander did not provide the normal `OS=Windows_NT` environment variable, so Integration + fault injection and Full automation self-test were classified as non-Windows and skipped. The machine independently reported `Environment.OSVersion.Platform=Win32NT` and `RuntimeInformation.IsOSPlatform(Windows)=True`.

The canonical script was not edited. With process-local `OS=Windows_NT` restored, `scripts/Verify-Release.ps1` passed 25/25: functions 613/613, explicit call sites 6494 / 0 uncovered, Core 79/79, Automation 20/20, Integration 89/89, self-test 11/11, strict builds/analyzers PASS.

## Release build

With the same standard Windows process marker restored, `scripts/Build-Release.ps1` passed completely, including App win-x64 compile/analyzers, ReadyToRun restore, self-contained ReadyToRun publish, and function-cache promotion.

Release ZIP: `artifacts/MHW-Manual-Mod-Manager-v8.8.0-win-x64.zip`
SHA-256: `54C53313567D96E0FE937746FE0F323E229717CE7CE296694FEC151F2E73FC79`

Conclusion: the fresh local Windows requirement for the repaired CAS boundary is satisfied. CAS integrity is CLOSED.