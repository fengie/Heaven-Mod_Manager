# Auto-updater C9 native metadata/rollback support — 2026-09-28

## Scope and current senior truth

- Repository: `fengie/mhw-mods`.
- Senior branch: `agent/auto-updater-20260928`.
- Exact rebased senior checkpoint: `1fbdd0619cc4e7bee00d4eeb70de1d4d488166d4`.
- C9 production/test implementation: `8cdf54d0bc4065a55124aef96c68b26de2a78f0c`.
- Support branch: `agent/support-updater-lr003-native-replacement-20260928`.
- Work executed on `heaven`; `heaven2` was deliberately not used.

C9 correctly made updater payload replacement fail closed for native `ReplaceFileW` errors 1176/1177: it preserves the updater backup and native replacement evidence and leaves the journal `RollbackRequired` rather than guessing through an ambiguous pathname state.

This support pass preserves that policy.

## Remaining gap found

C9's deterministic replacement backend was wired only to product payload replacement.

Two additional existing-file replacement boundaries still use `AtomicFileOps.ReplaceFromAsync`:
- publication of `product-files.json` and `release-install.json`;
- restoration of backed-up files during rollback.

The generic C9 apply catch already recognizes metadata-publication 1176/1177 as ambiguous, but there was no deterministic seam to prove that behavior.
More importantly, explicit `RollbackAsync` had no equivalent ambiguous-native catch. If rollback itself encountered 1176/1177, the native exception escaped while the journal could remain `AppliedAwaitingHealth`, even though the live namespace had entered an ambiguous recovery state.

## Support change

`UpdateInstaller` now accepts two additional optional test seams:
- `metadataReplaceBackend`;
- `rollbackReplaceBackend`.

Production callers leave both null and retain the existing Windows native backend.

Metadata publication passes its dedicated seam to the existing `AtomicFileOps` calls. C9's existing apply-level 1176/1177 fail-closed logic is unchanged.

Explicit rollback now catches only the already-defined ambiguous native 1176/1177 category, best-effort records `RollbackRequired`, logs preservation of recovery evidence, and rethrows. Other rollback error behavior is unchanged.

Rollback restoration passes its dedicated seam to `AtomicFileOps`.

## New regressions

### Product-manifest publication 1176

The fixture materializes the documented 1176 pathname state while publishing `product-files.json`.

Expected/proved:
- apply throws native error 1176;
- journal is `RollbackRequired`;
- independent updater backup remains;
- the live product-manifest pathname is absent;
- the staged atomic-replacement temp remains and contains the target manifest bytes;
- no speculative rollback is attempted.
### Install-marker publication 1177

The fixture lets product-manifest publication succeed, then materializes a 1177-style displaced old marker plus target replacement temp at `release-install.json`.

Expected/proved:
- apply throws native error 1177;
- journal is `RollbackRequired`;
- displaced old marker remains build 1;
- replacement temp remains build 2;
- updater backup remains;
- no speculative rollback is attempted.

### Explicit rollback 1176

A normal update first reaches `AppliedAwaitingHealth`. A subsequent explicit rollback then materializes 1176 while restoring the previous executable.

Expected/proved:
- rollback throws native error 1176;
- journal transitions from `AppliedAwaitingHealth` to `RollbackRequired`;
- rollback backup remains;
- ambiguous live executable pathname remains absent;
- replacement temp containing exact previous `OLD-APP` bytes remains.

This closes the stale-journal ambiguity without weakening C9's recovery policy.

## Verification on heaven

Environment:
- Windows x64;
- .NET SDK 10.0.401 from `C:\Users\Xxkan\.dotnet10`.

Results after rebasing onto the senior WPF-integration head:
- updater-focused classes: **69/69 PASS**;
- `UpdateInstallerTests`: **32/32 PASS**;
- full `MhwModManager.IntegrationTests`: **166/166 PASS**;
- strict whole-solution build with `-warnaserror`: **PASS, 0 warnings / 0 errors**;
- `git diff --check`: PASS.
The full integration run emitted only the repository's expected FunctionVerifier negative-fixture diagnostics; final xUnit execution was 166/166 green.

## Integration guidance

This branch is intentionally rebased through the senior WPF-integration head and retains C9's stricter recovery policy. Harvest it only onto a senior branch that still contains C9 or an equivalent fail-closed policy.

The senior branch has now integrated the WPF updater lifetime/client work while this support pass was running. The next independent boundary is packaging/publication plus its release-gate and disposable old-to-new/rollback verification; this support patch should not broaden into that work.

No `Verify-Release.ps1`, `Build-Release.ps1`, hosted Windows Release Gate, verification-cache promotion, release publication, or disposable old-to-new packaged installation claim is made here.

## Continuity

Preserve the permanent continuity constitution and LR-001 through LR-010. Exact-input verification provenance remains mandatory. The successor must require its own successor to recursively propagate the same continuity rules.

**Do not break the chain.**
