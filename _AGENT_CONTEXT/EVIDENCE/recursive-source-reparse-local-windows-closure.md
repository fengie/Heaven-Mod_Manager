# Recursive source reparse containment — local Windows evidence

- Date: 2026-09-28
- Device: heaven2 / Win32NT / Windows 10.0.26200
- .NET SDK: 10.0.401
- Source implementation commit: `f51f72927e8f90c264df1ef197ba6c9bbe2704de`
- Reconciled exact local verification source: `ba8b9a049b9b6e1c68cf24cbeb337b9e91d5dfd5`
- Branch: `agent/recursive-source-reparse-hardening-20260927`

## Test-first failure evidence

Before production repair, real Windows junction regressions failed:
- scanner: expected IOException, no exception thrown;
- adoption: expected IOException, no exception thrown;
- Smart Inbox: expected Imported=0, actual Imported=1.

## Post-fix focused evidence

- Integration/fault injection: 91/91 PASS
- Automation: 21/21 PASS
- strict builds for both changed test projects: 0 warnings, 0 errors.

## Full repository verification

`scripts/Verify-Release.ps1` with the standard process-local `OS=Windows_NT` marker after independently confirming Win32NT:
- 25 passed / 0 failed
- functions 614/614 after promotion
- explicit call sites 6512 / 0 uncovered
- trace gaps 0; parse errors 0
- Automation 21/21
- Integration/fault injection 91/91
- self-test 11/11
- strict whole solution/analyzers PASS.

## Release build

`scripts/Build-Release.ps1`:
- Core 79/79
- Automation 21/21
- Integration 91/91
- self-test 11/11
- ReadyToRun self-contained win-x64 publish PASS
- ReadyToRun fallback used: False
- ZIP SHA-256: `7B46BF7CBC85F4818B49D478613E3FE20F5F83E98E416F60E2D9F79D03E7F686`.

Hosted Windows Release Gate was **not executed** from this session. The workflow supports branch verification through `workflow_dispatch`, but the available GitHub connector has no dispatch action; `gh` is absent on `heaven2`; and the tool safety layer blocked credential extraction for a direct API dispatch. This local evidence must not be represented as hosted closure. The next agent/operator must dispatch the gate against the exact final branch HEAD and preserve its run ID/artifacts before closure.
