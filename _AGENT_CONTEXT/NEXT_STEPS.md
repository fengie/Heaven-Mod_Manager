# Next steps

## Immediate: verify the architecture / Explain Why candidate

The closed v8.8.0 Windows baseline remains commit
`5f6789af499fcc1afe6cb5d38244927bb02335fb` / run `36321128433`.
The current architecture candidate changes production source and is **not green until a
new Windows Release Gate run passes for the integrated main SHA**.

1. Integrate `agent/architecture-explain-why` into `main` without rewriting history.
2. Let `.github/workflows/windows-release-gate.yml` run the repository's exact
   verification and release scripts.
3. Fix real compile/test/trace/handoff failures. Do not weaken or bypass the gate.
4. When green, persist the exact source SHA, run ID, updated function inventory/test
   counts, release artifact hash, and hosted evidence into the continuity state.
5. Only then continue architecture work.

## What this candidate already establishes

- WPF no longer owns the Activity and Outfit/Coverage SQL projections.
- WPF no longer owns archive staging/wrapper-normalization filesystem code.
- Explain Why consumes `DeploymentPlanner` / `ConflictDecision`; there is no second
  compatibility engine.
- Overlaps expose exact-path winner/provider/rule/confidence/evidence/provenance details.
- Activity, Coverage, Import, and Overlap/Explain orchestration have feature partials,
  reducing the central MainWindow view-model file while preserving bindings.
- New regression tests cover structured Explain Why, presentation reads, and XAML bindings.

## After this candidate is fully green

Continue **incrementally**, in this order:

1. Convert the partial feature seams into actual page view models where cross-page
   coordination is no longer needed.
2. Extract additional cohesive persistence domains from `ManagerDatabase`, preserving
   explicit transaction ownership and deployment atomicity.
3. Split `MainWindow.xaml` along stable page boundaries after shared resource placement
   is made safe.
4. Refactor the startup composition graph only as a separate change; evaluate Generic
   Host/DI then rather than mixing lifecycle changes into this milestone.
5. Finish nearby workflows whose backend is already mature (profile diff / collection
   import / update migration) only after the architecture checkpoint remains green.

Do **not** start FOMOD support or a broad enhanced-game-adapter redesign before the
architecture/explainability checkpoint is closed.

## Invariants to preserve

- immutable source/CAS assumptions;
- whole-plan preflight and per-write revalidation;
- journal-before-mutation and one logical SQLite commit point for deployment state;
- rollback/recovery refusing to overwrite unknown external edits;
- Windows path/archive safety;
- explicit human rules outranking inference;
- generic-game fail-closed behavior;
- verification promotion only after the required gate passes.

## Before finishing any future repository task

Read the agent/continuity instructions, update `_AGENT_CONTEXT` with materially learned
state, keep evidence tied to an exact SHA, run `scripts/Test-AgentHandoff.ps1`, commit
handoff context with the code it describes, and tell the next agent to repeat the process.
Do not break the chain.
