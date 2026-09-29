# Startup mod discovery after updater relaunch — 2026-09-29

## Task

Fix the user-visible case where the populated mod library is empty on the first application launch after an automatic update, but appears after the manager is closed and reopened.

## PLUGIN-PREFLIGHT

- Task reviewed: application startup path discovery, updater handoff/restart identity, regression coverage, version/release integration.
- Capabilities considered: repository-native Heaven Local Bridge execution, GitHub repository connector, generic remote desktop control.
- Current-runtime discovery: no Heaven Local Bridge plugin/skill is exposed in this Chat session; GitHub repository read/write/PR/workflow capabilities are available.
- Selected route: normal Chat with the GitHub repository connector. No ChatGPT Work handoff and no Remote Desktop Commander fallback.
- Reason: this route can make and verify the repository changes while preserving the user's no-Work preference and the repository's plugin-first/fallback rules.

## Root cause

`AppPaths.Discover()` used `AppContext.BaseDirectory` as the manager home whenever `MOD_MANAGER_HOME` / `MHW_MANAGER_HOME` were absent. Updater-driven launches execute the packaged binary from its install/release directory, so that fallback can point startup at an install-local `Mods` / `State` tree instead of the populated canonical manager home. The diagnostic-root path already had release-layout recovery, but application data-root discovery did not share it.

The updater request also did not persist the manager-home path explicitly; restart correctness depended on inherited process environment.

## Repair

- Centralize manager-home fallback in `AppPaths.ResolveToolRoot`, preserving explicit environment configuration first and recovering the project root from the known `<root>/release/<version>` layout when applicable.
- Make startup export the resolved canonical `MOD_MANAGER_HOME` for later child processes.
- Persist `ManagerHomeRoot` in `UpdateApplyRequest`; pass `s.Paths.ToolRoot` when preparing an updater handoff.
- Make the updater helper restore both manager-home environment variable names before starting the updated or rolled-back application.
- Keep existing update requests backward-compatible by making the new serialized field optional.
- Add regression coverage for release-layout recovery, explicit-home precedence, ordinary installs, and updater request persistence.

## Release identity

This is a shipped behavior fix and advances the application to **v8.8.16**. `VERSION.txt`, `Directory.Build.props`, `README.md`, and `CHANGELOG.md` are updated with the same release identity.

## Verification obligation

Do not call the fix complete until the exact candidate passes the applicable Windows/PR checks, is reconciled with current canonical `main`, lands on remote `main`, and the required updater release/public mirror publication is verified.
