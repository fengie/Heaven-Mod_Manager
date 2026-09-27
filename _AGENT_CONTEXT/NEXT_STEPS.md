# Next steps

## Immediate: rerun fixed Games candidate

Production source commit `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34` repairs the only failure from hosted
run `36330808544`.

Previous run facts:

- exact commit: `1aa8cff1d06ba3b97dfe362655fe07e1c5758514`
- verifier: **24/25**
- sole failure: function fingerprint scan
- gap: `MainWindowViewModel.ScanInstalledGames()`
- trace gaps: **1**
- uncovered explicit call sites: **7**
- relaxed/strict compile: PASS, 0 warnings/errors
- Integration/fault injection: **66/66**

Current fix:

- adds the mandatory `MasterDebugLog.BeginMethod()` entry scope to the moved
  scan command;
- adds a regression assertion for that trace;
- changes no game/discovery/switch/restart/deployment behavior.

Now:

1. Push the fix + adjacent continuity checkpoint.
2. Run the full Windows Release Gate against the exact integrated fix.
3. Require handoff preflight, zero trace/call-site gaps, all strict builds,
   tests/self-test, win-x64 analyzers and ReadyToRun publish to pass.
4. Fix any real new failure without weakening gates.
5. If fully green, persist exact verification evidence and close Games with a
   docs-only `[skip ci]` checkpoint.
6. Do not begin another architecture slice before Games is closed.

## Games architecture boundary

- `GamesPageViewModel`: registry `Load()` + observable rows only.
- `MainWindowViewModel.Games`: discovery/add/configure/switch/restart shell
  commands and busy/status coordination.
- `SelectedGame`: shell-owned.
- switching: persists active profile, launches a fresh manager process, shuts
  down current WPF app.
- `AppPaths.Discover`: resolves active game before constructing game-scoped
  services.

## Last closed evidence

Profiles remains the last closed source boundary until Games reruns green:

- verified commit: `04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8`
- run: `36328183152`
- 25/25, 613/613 fingerprints
- Core 79/79, Automation 20/20, Integration 65/65
- self-test 11/11, ReadyToRun PASS

Do not start FOMOD, IssueSuspects extraction, XAML decomposition, Generic Host /
DI migration, broad AppServices changes, enhanced-adapter redesign, or broad
ManagerDatabase work while this boundary is open.

Before every handoff: keep `_AGENT_CONTEXT` current, push stable checkpoints,
and explicitly instruct the next agent to continue the same continuity practice.
