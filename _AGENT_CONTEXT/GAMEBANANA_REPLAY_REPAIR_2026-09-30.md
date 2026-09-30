# GameBanana replay repair — 2026-09-30

## Incident

PR #387 (`5f0a9bb3b6168b966b203fdb257c31b71ce7a12c`) replayed the nine-file GameBanana catalog tranche by appending an older implementation in front of the already-correct files on its current-main base instead of replacing those files. The merge therefore produced duplicate C# declarations / mid-file `using` directives and concatenated JSON fixtures.

Heaven exact-SHA job `job-20260930T040400Z-moddb-current-main-gate` caught the canonical break at `9676bd18a37873b1a7fd1128fec4549d24b0ea20` before tests could start. The first compiler errors were CS1529 in `GameBananaCatalogNormalizer.cs`, `GameBananaTransport.cs`, and `GameBananaCatalogProvider.cs`.

## Repair

Canonical repair commit: `36f5f6c136cb5927a49ae9976b50ad74e1cfd26c`.

The repair is mechanical and tree-proven: all nine affected paths were restored byte-for-byte to PR #387's exact base tree `0a43fcfe0324bb592b24760c15d0e56fde084265`. This preserves the newer provider behavior that already existed on main before the replay, including the provider-authorized mod-level assisted target `https://gamebanana.com/mods/download/{modId}`, while removing the appended older copies.

Restored paths:

- `src/MhwModManager.Core/GameBananaCatalogNormalizer.cs`
- `src/MhwModManager.Core/GameBananaCatalogPolicy.cs`
- `src/MhwModManager.Core/GameBananaCatalogProvider.cs`
- `src/MhwModManager.Core/GameBananaTransport.cs`
- `tests/MhwModManager.Tests/Fixtures/Catalog/GameBanana/mod.json`
- `tests/MhwModManager.Tests/Fixtures/Catalog/GameBanana/new-mods.json`
- `tests/MhwModManager.Tests/GameBananaCatalogNormalizerTests.cs`
- `tests/MhwModManager.Tests/GameBananaCatalogProviderTests.cs`
- `tests/MhwModManager.Tests/GameBananaTransportTests.cs`

For every path, the repaired blob SHA equals the corresponding blob SHA at PR #387's base.

## Verification

Pinned Heaven verification job: `job-20260930T042300Z-gamebanana-repair-full-gate`.

It checks exact source `36f5f6c136cb5927a49ae9976b50ad74e1cfd26c` in an isolated worktree and runs:

1. `MhwModManager.Tests` Release tests;
2. `MhwModManager.IntegrationTests` Release tests;
3. `scripts/testing/Test-CiSecurityPolicy.ps1`;
4. full `scripts/release/Verify-Release.ps1`.

Do not extend that evidence to later main commits. Do not call the repair fully verified until the result is green.

## Prevention

A replay/integration operation on already-tracked paths must prove replacement semantics and inspect the resulting canonical tree. Large all-additions diffs on paths that already exist are suspicious. Before integration, parse/build the fully assembled candidate and validate structured fixtures such as JSON. After integration, prove the intended blobs/content survived in the canonical tree; ancestry alone is insufficient.

See LR-035 (explicit integration readiness), LR-037 (canonical tree proof), and LR-038 (replay replacement / assembled-artifact verification).
