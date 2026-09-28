# Recursive source reparse containment — local Windows evidence

- Date: 2026-09-28
- Device: heaven2 / Win32NT / Windows 10.0.26200
- .NET SDK: 10.0.401
- Source implementation commit: `f51f72927e8f90c264df1ef197ba6c9bbe2704de`
- First-pass reconciled local verification source: `ba8b9a049b9b6e1c68cf24cbeb337b9e91d5dfd5` (superseded by follow-up evidence below)
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

## Adversarial stress follow-up

Exact follow-up source: `742484ba7a6ff07d12c0cfa1ea1a46a1b1205b4a`.

Pre-fix proof against candidate `1c453f5f9c813a85bca53657c0ee30b47d2d15ab` with only the new regressions overlaid:
- Integration: **94 total / 1 failed** — scanner package-root junction was accepted instead of throwing.
- Automation: **24 total / 1 failed** — safe-tree Smart Inbox classification was **Mixed** instead of legacy **Texture**.

Post-fix focused Windows evidence:
- Integration **94/94 PASS**
- Automation **24/24 PASS**
- changed test projects build with 0 warnings / 0 errors.

Post-fix full repository verification:
- `Verify-Release.ps1`: **25/25 PASS**
- functions **615/615** after promotion
- explicit call sites **6517 / 0 uncovered**
- trace gaps / parse errors: **0 / 0**
- Core **79/79**
- Automation **24/24**
- Integration/fault injection **94/94**
- self-test **11/11**
- strict whole solution/analyzers PASS.

Post-fix release build:
- `Build-Release.ps1` PASS
- win-x64 compile/analyzers PASS
- ReadyToRun self-contained publish PASS
- ZIP SHA-256: `665836D7BED41CD83925FD956E987E9D64D1DE618BF238157E13291F5B7731B6`.

Real file-symlink creation was attempted separately on `heaven2` and failed with `You do not have sufficient privilege to perform this operation.` A dedicated file-reparse leaf regression therefore remains environment-blocked and is not counted as executed.

Hosted Windows Release Gate was **not executed** from this session. Local verification does not substitute for hosted-runner evidence. The next agent/operator must dispatch the gate against the exact final branch HEAD and preserve its run ID/artifacts before closure.
