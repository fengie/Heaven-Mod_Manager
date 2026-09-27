# Next steps

## Immediate: re-audit remaining MainWindow responsibilities

Games is closed at exact commit `106a4569b572473394aa075bcfa5d9c03f2fe44d`, run `36331057943`.

Four presentation/read-state seams are now independently Windows-verified:

- Activity
- Coverage
- Profiles
- Games

Do not assume every remaining responsibility deserves the same pattern.

### First action

Inspect the remaining `MainWindowViewModel` methods/collections and classify
them by coupling:

- passive presentation/read state;
- shell/global coordination;
- cross-feature application workflow;
- persistence/domain behavior that belongs below WPF.

Do **not** force `IssueSuspects` into a page model while it still updates
`ModRowViewModel` issue badges, filtered views, header counters, and diagnosis
workflows.

### If another clean presentation seam exists

Take exactly one slice:

1. preserve the existing XAML binding surface;
2. keep shell-global busy/status/lifetime behavior in `MainWindowViewModel`;
3. add a seam/binding regression guard;
4. source commit -> adjacent unverified continuity commit -> push;
5. require a fresh full Windows Release Gate;
6. close only after exact evidence is persisted.

### If no clean presentation seam remains

Do not invent one. Start a separately scoped **ManagerDatabase repository
extraction design**:

1. inventory ManagerDatabase methods by domain;
2. map every method that participates in explicit multi-statement transactions;
3. identify read-only/query seams first;
4. preserve the same connection/transaction object for each atomic operation;
5. extract one domain repository at a time;
6. verify each production boundary independently.

Do not start implementation of a database split until the transaction-boundary
map is concrete enough to prove SQLite atomicity will not be fragmented.

## Closed Games evidence

- exact verified commit: `106a4569b572473394aa075bcfa5d9c03f2fe44d`
- production source: `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`
- run: `36331057943`
- verifier/fingerprints: **25/25**, **615/615**
- explicit call sites: **6389**, uncovered **0**
- trace gaps / parse errors: **0 / 0**
- Core **79/79**, Automation **20/20**, Integration **66/66**
- self-test **11/11**
- win-x64 analyzers: PASS
- ReadyToRun publish: PASS
- SHA-256: `4872ABDA6D548CB9F97668AF1A3019AC44B146A9A66876F92B065D7009455189`
- evidence/cache persistence: `750a3232ad9ac82bd1587ddd903709b886c9b8bb`

## Still separate later checkpoints

- MainWindow.xaml page/view decomposition
- AppServices / Generic Host / DI lifetime migration
- FOMOD support
- broad enhanced-game adapter redesign

## Invariants

Preserve deployment journal/CAS/rollback/TOCTOU/path safety, one logical SQLite
deployment commit point, human-rule precedence, generic fail-closed behavior,
planner-backed Explain Why, game-switch restart semantics, and verification
promotion only after exact matching gates.

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and explicitly instruct the next agent to continue the same continuity practice.
