# Next steps

## Immediate: verify Profiles read/list candidate

Production source commit `19a1ad4a4e665e4ce7f38586dea5f08f1c3acdf0` implements the Profiles read/list
state extraction.

Do not begin another architecture slice yet.

1. Run the full Windows Release Gate against the exact integrated Profiles
   checkpoint.
2. Require continuity preflight, function scan, strict builds/analyzers,
   Core/Automation/Integration tests, self-test, win-x64 compile, and
   ReadyToRun publish to pass.
3. Fix real failures rather than weakening gates or manually promoting caches.
4. If green, persist the exact verified SHA/run, function inventory, test
   counts, release SHA-256, and promoted cache evidence.
5. Update the canonical handoff files to mark Profiles CLOSED.
6. Only then choose another low-coupling architecture seam.

## Profiles candidate design

- `ProfilesPageViewModel` owns profile list reads and collection state.
- `MainWindowViewModel.Profiles` aliases `ProfilesPage.Rows`.
- `RefreshProfilesCommand` and `RunBusy` remain shell-owned.
- `SelectedProfile`, Stage/Save/Delete profile mutations, mod staging, and
  global `StatusText` remain shell-owned.
- A source-level integration guard preserves the binding seam.

## Last closed evidence

- Coverage exact verified commit: `e3ed3be730000d1829b02e5d2d29b3f23ca52d94`
- Coverage production source: `855f6e5eb4998aa442538636b76f5c644146eb6a`
- run: `36327634813`
- verifier/fingerprints: **25/25**, **611/611**
- Core **79/79**, Automation **20/20**, Integration **64/64**
- self-test **11/11**
- ReadyToRun win-x64 publish: PASS
- SHA-256: `40B47B6E3C9CF21A0945095FE28E118540B415FBD9177190BDB99FE80C9657A8`

## After Profiles is independently green

Continue one seam at a time. A likely next candidate is a low-coupling Games
presentation/state seam or another isolated page read model; avoid IssueSuspects
until its coupling to Mod rows/effective-state badges is deliberately designed.
After page seams, move to cohesive `ManagerDatabase` persistence extraction
while preserving explicit transaction ownership.

Keep XAML decomposition and Generic Host / DI lifetime migration as separate
later checkpoints.

Do not start FOMOD or broad enhanced-game adapter redesign.

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, and verification promotion only after exact gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and instruct the next agent to continue the same continuity practice.
