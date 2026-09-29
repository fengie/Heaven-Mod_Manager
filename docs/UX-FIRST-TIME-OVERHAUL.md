# First-Time User UX Overhaul

## Goal

Make the normal experience understandable to a reasonably computer-literate person who has never used a mod manager. Core workflows should explain what the app is showing, what the user should do next, and what an action will do before it is clicked. Advanced diagnostics and implementation detail remain available through progressive disclosure.

## Audit

### Critical confusion
- Primary launch and deployment actions use ambiguous or developer-oriented labels such as `JUST PLAY`, `Apply`, and `Vanilla`.
- Core states expose internal concepts such as effective, composed, superseded, staged, overlaps, provider, resolver score, and revalidation.
- Conflict resolution asks users to reason about provider/family mechanics before it explains the human choice: which mod should win, or whether packages are a main mod plus add-ons.
- The overlap explorer exposes implementation fields as first-class content.

### Major friction
- The Dashboard gives maintenance/debug actions similar prominence to the normal install/apply/play path.
- Mod-library bulk controls and row details use maintenance terms such as re-index and internal configuration.
- The toolbar assumes the user already knows the workflow.
- Activity/support actions are framed in transaction terminology instead of user outcomes.

### Minor friction
- Status labels are terse (`ON`, `OFF`, `PARTIAL`) and pending changes are described as staged.
- Some empty/filter states describe internal views rather than the recovery action.
- Technical table headings dominate screens intended for normal use.

### Cosmetic/readability
- Common controls can use slightly larger minimum targets while remaining comfortable on laptop displays.
- Secondary technical information should be visually quieter than names, status, problems, and primary actions.

### Advanced-user improvements
- Preserve exact paths, reason codes, confidence, provenance, family roles, transaction IDs, and diagnostics.
- Make advanced detail intentional through progressive disclosure.
- Preserve deployment, rollback, conflict, update, and diagnostics behavior.

## Implementation sequence
1. Rewrite top-level navigation, buttons, dashboard, mod statuses, conflict copy, history/support wording, and shared-file wording in plain language.
2. Improve shared control sizing and beginner-readable state labels without changing backend semantics.
3. Preserve technical details under advanced/diagnostic surfaces and tooltips.
4. Update release identity and user-facing documentation.
5. Run XAML/build/test/static verification and reconcile with current `main` before integration.

## Acceptance checks
- A new user can identify Install Mod, Apply Mod Changes, Launch Game, and Launch Without Mods without documentation.
- Core statuses use ordinary language.
- Conflict UI explains the user-level choice before technical evidence.
- Empty states say what belongs there and how to proceed.
- Technical diagnostics remain accessible.
- No deployment/conflict/rollback behavior is intentionally removed or weakened.
- Multiple-window-size/readability verification is required before release closure.
