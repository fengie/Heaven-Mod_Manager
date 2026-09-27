# Next steps

## Immediate: verify Coverage page-view-model candidate

Production source commit `855f6e5eb4998aa442538636b76f5c644146eb6a` implements the Coverage extraction.

Do not begin another architecture slice yet.

1. Run the full Windows Release Gate against the exact integrated Coverage
   checkpoint.
2. Require the continuity preflight, function scan, strict builds/analyzers,
   Core/Automation/Integration tests, self-test, win-x64 compile, and
   ReadyToRun publish to pass.
3. Fix real failures rather than weakening gates or manually promoting caches.
4. If green, persist the exact verified SHA/run, function inventory, test
   counts, release SHA-256, and promoted cache evidence.
5. Update `CURRENT_REVISION.json`, `CURRENT_STATE.md`, `VERIFICATION.md`,
   and `NEXT-AGENT-START-HERE.md` to mark Coverage CLOSED.
6. Only then choose one next low-coupling architecture seam.

## Coverage candidate design

- `CoveragePageViewModel` owns coverage reads, `OutfitRow` mapping, and rows.
- `MainWindowViewModel.OutfitRows` aliases `Coverage.Rows`, preserving XAML.
- `RefreshOutfitsCommand`, `RunBusy`, and shell-global `StatusText` remain
  in `MainWindowViewModel`.
- Generic no-semantic-coverage status is returned to the shell instead of the
  page model owning global status.
- A source-level integration guard preserves the binding seam.

## Last closed evidence

- Activity exact verified commit: `5eab48f0a2139e3aee96a7c71e4466d2e1168877`
- Activity production source: `e2396c7c91c5d8d88fe229603689539b5cdfb2da`
- run: `36325994246`
- verifier/fingerprints: **25/25**, **609/609**
- Core **79/79**, Automation **20/20**, Integration **63/63**
- self-test **11/11**
- ReadyToRun win-x64 publish: PASS
- SHA-256: `7ED67747DADE8BD3E56D30139BF39886F5E0A293F9CAE9B2887D06D720F589AF`

## After Coverage is independently green

Continue one seam at a time. Prefer another low-coupling page-view-model seam
before moving into cohesive `ManagerDatabase` persistence extraction. Preserve
explicit transaction ownership. XAML page extraction and Generic Host / DI
lifetime migration should remain separate later checkpoints.

Do not start FOMOD or broad enhanced-game adapter redesign during these slices.

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, and verification promotion only after exact gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and instruct the next agent to continue the same continuity practice.
