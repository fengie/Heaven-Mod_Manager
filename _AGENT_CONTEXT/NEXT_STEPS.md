# Next steps

## Immediate: verify Games list-presentation candidate

Production source commit `ba6b6b32bfb754d25afe3349c2692a9279954c4a` implements the Games list-state
extraction.

Do not begin another source slice yet.

1. Run the full Windows Release Gate against the exact integrated Games
   checkpoint.
2. Require agent-handoff preflight, function scan, strict builds/analyzers,
   Core/Automation/Integration tests, self-test, win-x64 compile, and
   ReadyToRun publish to pass.
3. Fix real failures rather than weakening gates or manually promoting caches.
4. If green, persist the exact verified SHA/run, function inventory,
   explicit/uncovered call-site counts, test counts, release SHA-256, and
   promoted cache evidence.
5. Update the canonical handoff files to mark Games CLOSED.
6. Only then choose another architecture seam.

## Games boundary confirmed by inspection

- `GamesPageViewModel`: registry `Load()` + observable rows only.
- `MainWindowViewModel.Games`: discovery/add/configure/switch/restart shell
  commands and busy/status coordination.
- `SelectedGame`: still shell-owned.
- `AppPaths.Discover`: still resolves the active registry profile before
  constructing all game-scoped services.
- switching still uses full process restart rather than in-process service
  graph mutation.

## Last closed evidence

- Profiles exact verified commit: `04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8`
- Profiles production source: `19a1ad4a4e665e4ce7f38586dea5f08f1c3acdf0`
- run: `36328183152`
- verifier/fingerprints: **25/25**, **613/613**
- explicit call sites: **6385**, uncovered **0**
- Core **79/79**, Automation **20/20**, Integration **65/65**
- self-test **11/11**
- ReadyToRun win-x64 publish: PASS
- SHA-256: `A150FFA7B832C56535A9CA19DCFEDD7640C3BE1F8E9ACB4D40881CB8C69F93B8`

## After Games is independently green

Reassess the remaining MainWindow responsibilities before choosing another
slice. Do not force `IssueSuspects` into a page model while it still mutates
Mod-row issue badges and aggregate shell counters.

After clean presentation seams, move toward cohesive `ManagerDatabase`
repository extraction while preserving explicit connection/transaction
ownership and the single logical deployment commit point.

Keep these as separate later checkpoints:

- MainWindow.xaml page/view decomposition
- AppServices / Generic Host / DI lifetime migration
- FOMOD support
- broad enhanced-game adapter redesign

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, game-switch restart semantics, and verification
promotion only after exact gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and explicitly instruct the next agent to continue the same continuity practice.
