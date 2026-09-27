# Next steps

## Immediate: Coverage page-view-model slice only

The Activity slice is closed at exact commit `5eab48f0a2139e3aee96a7c71e4466d2e1168877`, run
`36325994246`.

Implement one next responsibility seam:

1. Add a `CoveragePageViewModel` that owns the coverage read projection,
   `OutfitRow` mapping, and coverage row collection.
2. Keep the current `OutfitRows` binding surface by aliasing the page model's
   row collection from `MainWindowViewModel`.
3. Keep `RefreshOutfitsCommand`, `RunBusy`, and global `StatusText`
   coordination in `MainWindowViewModel`.
4. For generic games with no semantic coverage, let the page model return a
   status/result to the shell rather than directly owning global status.
5. Add a binding/seam regression guard.
6. Push the source + adjacent continuity checkpoint and require a full fresh
   Windows Release Gate before another page extraction.

## Closed evidence

- Activity exact verified commit: `5eab48f0a2139e3aee96a7c71e4466d2e1168877`
- Activity production source: `e2396c7c91c5d8d88fe229603689539b5cdfb2da`
- run: `36325994246`
- verifier/fingerprints: **25/25**, **609/609**
- Core **79/79**, Automation **20/20**, Integration **63/63**
- self-test **11/11**
- ReadyToRun win-x64 publish: PASS
- SHA-256: `7ED67747DADE8BD3E56D30139BF39886F5E0A293F9CAE9B2887D06D720F589AF`

## After Coverage is independently green

Continue one seam at a time. Candidates include other page view models, then
cohesive `ManagerDatabase` persistence slices that preserve explicit
transaction ownership, then XAML page extraction. Treat Generic Host / DI
lifetime migration as a separate later checkpoint.

Do not start FOMOD or broad enhanced-game adapter redesign during these slices.

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, and verification promotion only after exact gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and instruct the next agent to continue the same continuity practice.
