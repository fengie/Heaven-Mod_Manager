# Next steps

## Immediate: Profiles read/list page-view-model slice only

Coverage is closed at exact commit `e3ed3be730000d1829b02e5d2d29b3f23ca52d94`, run `36327634813`.

Implement one next low-coupling responsibility seam:

1. Add a `ProfilesPageViewModel` that owns profile list reads and profile row
   collection state.
2. Keep the current `Profiles` binding surface by aliasing the page model's
   collection from `MainWindowViewModel`.
3. Keep `RefreshProfilesCommand`, `StageProfile`, `SaveProfile`,
   delete/profile mutation commands, `RunBusy`, and global `StatusText`
   coordination in `MainWindowViewModel`.
4. Do not move mod staging/application logic into the page model.
5. Add a binding/seam regression guard.
6. Push source + adjacent continuity checkpoint and require a full fresh
   Windows Release Gate before another extraction.

## Closed Coverage evidence

- exact verified commit: `e3ed3be730000d1829b02e5d2d29b3f23ca52d94`
- production source: `855f6e5eb4998aa442538636b76f5c644146eb6a`
- run: `36327634813`
- verifier/fingerprints: **25/25**, **611/611**
- Core **79/79**, Automation **20/20**, Integration **64/64**
- self-test **11/11**
- ReadyToRun win-x64 publish: PASS
- SHA-256: `40B47B6E3C9CF21A0945095FE28E118540B415FBD9177190BDB99FE80C9657A8`
- evidence/cache persistence: `e62e7ad5d93cdb6c6ae3d6e8562d6fae667e9c02`

## After Profiles is independently green

Continue one seam at a time. Prefer another page-view-model seam before moving
into cohesive `ManagerDatabase` persistence extraction. Preserve explicit
transaction ownership. XAML page extraction and Generic Host / DI lifetime
migration should remain separate later checkpoints.

Microsoft's current .NET/MVVM guidance supports constructor injection and
modular view models, but a Generic Host migration changes application lifetime
and service composition and should not be mixed into these page extraction
checkpoints.

Do not start FOMOD or broad enhanced-game adapter redesign during these slices.

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, and verification promotion only after exact gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and instruct the next agent to continue the same continuity practice.
