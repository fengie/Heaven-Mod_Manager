# Automation and whole-application testing (v8.6)

## Automation policy

The normal path is intentionally hands-off:

1. Startup recovers any interrupted deployment first.
2. Smart Inbox imports safe ZIP/RAR/7z/folders from `Inbox\`, normalizes one wrapper folder, refreshes Nexus/local lineage, assigns categories, and moves the original inbox item to `Inbox\Processed\`.
3. Disabled exact duplicates and conclusively superseded source packages can be archived into `Mods Archive\`; they are never deleted.
4. `JUST PLAY` applies staged changes, adopts trackable loose `nativePC` files, creates a rolling save/state snapshot, checks dependencies/health/direct-replacement conflicts, launches MHW, observes the startup window, records per-mod launch history, and marks the setup Last Known Good when startup survives.
5. Human interruption is reserved for a blocker the resolver cannot safely infer: direct structural alternatives, a missing required loader/file, corrupted state, or a recovery/integrity failure.

## Convenience subsystems

- Save snapshots and Last Known Good
- Change timeline
- Smart Inbox
- Update diffing for superseded/Nexus revisions
- Content-derived categories
- Dependency doctor
- Safe duplicate/superseded archiving
- Collection recipe export
- Per-mod local trust history
- Effective-file provenance inspector and asset heatmap service
- Outfit preset inference
- Game-update impact report
- Automatic startup crash bisector for newly enabled mods
- Vanilla/safe mode
- Launch-time health gate

`MHW_SAVE_PATH` may be set to an exact `SAVEDATA1000` path. Otherwise the backup service searches normal Steam userdata locations for app 582010.

## One-go verification

Run either:

```text
Verify.bat
```

or:

```text
Test Everything.bat
```

The harness intentionally does **not** stop at the first failing subsystem. It performs restore, relaxed whole-solution compile, strict per-project compile/analyzer passes, strict whole-solution compile, core unit tests, automation unit tests, Windows integration/fault-injection tests, and the standalone automation self-test.

Every stage has its own raw log and build stages have MSBuild `.binlog` files. At the end it always writes:

```text
BuildLogs\verification-report-YYYYMMDD-HHMMSS.md
BuildLogs\verification-report-YYYYMMDD-HHMMSS.json
```

The Markdown report contains a table for every stage plus extracted compiler/analyzer/assertion/exception lines for failed stages. The JSON report is machine-readable. The standalone self-test also emits its own Markdown/JSON pair.

## Testability design

Convenience logic lives in `MhwModManager.Automation` rather than directly inside WPF. Pure policy (category inference, crash bisection, outfit presets) can be tested without a game install. Filesystem/database services are exercised against temporary directories and temporary SQLite databases. The WPF layer is kept primarily as orchestration and presentation.

The self-test never points at the user's live MHW install. It creates isolated temporary state and deletes it at the end.
