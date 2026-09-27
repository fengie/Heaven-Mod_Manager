# Next steps

## Immediate: inspect Games presentation/state seam

Profiles is closed at exact commit `04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8`, run `36328183152`.

Do not broaden the next slice before inspecting the current game-management flow.

If the seam remains low-coupling:

1. Add a small Games page/presentation view model that owns loading and exposing
   the game-profile list only.
2. Preserve the existing `Games` binding surface by aliasing its collection
   from `MainWindowViewModel`.
3. Keep `SelectedGame`, game switching, AppPaths/service reconstruction,
   application lifetime, `RunBusy`, and global `StatusText` in the shell /
   composition root unless source inspection proves a narrower safe boundary.
4. Add a binding/seam regression guard.
5. Push source + adjacent continuity checkpoint.
6. Require a full fresh Windows Release Gate before any further extraction.

If Games is more coupled than expected, do not force the abstraction. Record
the coupling and choose another low-risk seam instead.

## Closed Profiles evidence

- exact verified commit: `04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8`
- production source: `19a1ad4a4e665e4ce7f38586dea5f08f1c3acdf0`
- run: `36328183152`
- verifier/fingerprints: **25/25**, **613/613**
- explicit call sites: **6385**, uncovered **0**
- Core **79/79**, Automation **20/20**, Integration **65/65**
- self-test **11/11**
- ReadyToRun win-x64 publish: PASS
- SHA-256: `A150FFA7B832C56535A9CA19DCFEDD7640C3BE1F8E9ACB4D40881CB8C69F93B8`
- evidence/cache persistence: `122bcdb525bb432e73dff6f5887a63e065246964`

## Architecture order after page seams

Continue one independently verified seam at a time. Defer `IssueSuspects`
until its coupling to Mod rows/effective-state badges is deliberately designed.

After the remaining clean page seams, move to cohesive `ManagerDatabase`
repository extraction while preserving explicit transaction ownership and the
single logical deployment commit point.

Keep these as separate later checkpoints:

- MainWindow.xaml page/view decomposition
- AppServices / Generic Host / DI lifetime migration
- FOMOD support
- broad enhanced-game adapter redesign

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, and verification promotion only after exact gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and explicitly instruct the next agent to continue the same continuity practice.
