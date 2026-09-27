# Next steps

## Immediate: verify the Activity page-view-model slice

The malformed intermediate Activity run `36325764389` is superseded by the corrected source below. The first slice is now implemented in production source commit
`e2396c7c91c5d8d88fe229603689539b5cdfb2da` and is awaiting a fresh hosted Windows gate. Do not extract a
second page until this source boundary is green and recorded.

The architecture / Explain Why checkpoint is closed at exact commit
`9717a22d3338f77e63cd409a80d2ec5fc3c924f2` by hosted Windows run `36325133722`.

Proceed **one independently verifiable slice at a time**:

1. Convert the existing Activity partial seam into a real
   `ActivityPageViewModel`.
2. Preserve the current `ActivityRows` / `RefreshActivityCommand` binding
   surface during that first extraction so the change is responsibility-only.
3. Keep `MainWindowViewModel.RunBusy` as the cross-page/global operation
   coordinator for now; the page model should own Activity read projection and
   row state, not application-wide busy state.
4. Run the exact Windows Release Gate and close that source boundary before
   extracting another page.
5. Then repeat for another low-coupling seam (Coverage is a likely candidate),
   followed by persistence decomposition, XAML page extraction, and only later
   startup/DI lifetime changes.

## Closed checkpoint evidence

- verified exact commit: `9717a22d3338f77e63cd409a80d2ec5fc3c924f2`
- production source commit: `098d617bcb3dcdd044e3fdb8319ba506c97082af`
- run: `36325133722`
- verifier: **25/25**
- fingerprints: **607/607**
- Core **79/79**, Automation **20/20**, Integration/fault injection **62/62**
- self-test **11/11**
- ReadyToRun self-contained win-x64 publish: PASS
- artifact SHA-256: `DF87A48716596ABFFF545DD6C73BAAE02954167424908850D943BFFA3833A2D6`

## Architecture order after Activity

1. Continue actual page-view-model extraction where cross-page coupling is low.
2. Extract cohesive persistence domains from `ManagerDatabase`, keeping
   explicit transaction ownership visible and deployment atomicity unchanged.
3. Split `MainWindow.xaml` only along stable page boundaries after view-model
   seams are real.
4. Treat Generic Host / DI lifetime migration as its own later change. Do not
   combine desktop lifetime semantics with page/database decomposition.
5. Finish nearby mature workflows only after each architecture checkpoint is
   independently green.

Do **not** start FOMOD support or a broad enhanced-game-adapter redesign during
these architecture slices.

## Invariants to preserve

- immutable source/CAS assumptions;
- whole-plan preflight and per-write revalidation;
- journal-before-mutation and one logical SQLite commit point for deployment state;
- rollback/recovery refusing to overwrite unknown external edits;
- Windows path/archive safety;
- explicit human rules outranking inference;
- generic-game fail-closed behavior;
- Explain Why consuming the real DeploymentPlanner/ConflictDecision path;
- verification promotion only after the required gate passes.

## Before finishing any future repository task

Read the agent/continuity instructions, update `_AGENT_CONTEXT` with materially
learned state, keep evidence tied to an exact SHA, run
`scripts/Test-AgentHandoff.ps1`, commit handoff context with the code it
describes, push stable checkpoints, and tell the next agent to repeat this
practice. Do not break the chain.
