# Installed WPF mod lifecycle acceptance

InstalledWpfModLifecycleE2ETests is an xUnit v3 suite using real Windows UI Automation on a runnable WPF application published from an exact source revision. It does not call view-model commands as a substitute for UI testing.

## Functional tests

1. Create a GUID-named temporary manager home and two synthetic registered games with harmless executable placeholders (never launched), empty game Mods folders, and preserved sentinel bytes.
2. Import a one-mod ZIP through the app's native archive picker. Verify its entry appears disabled in the actual Mods list, source SHA-256 is unchanged and no live game files are written.
3. In a separate fixture, process the game Inbox using the actual Dashboard More tools UI. Verify the extracted package and visible checkbox.
4. Toggle the imported mod, Preview Mod Changes and prove the preview is read-only, Apply Changes and prove exact fake-game target bytes, then Undo and prove baseline restoration.
5. Switch between the two registered fake games through the real WPF selector without restarting the process or altering any sentinel bytes.

The normal unit/integration runs report the two opt-in UI tests as skipped, never as passing. The hosted acceptance workflow must execute all three tests (two UI tests and a guard test), with zero skips.

## Isolation and evidence

The workflow runs only on disposable GitHub-hosted windows-2025 VMs, not on a self-hosted personal machine. Both workflow and test enforce a hosted-Windows opt-in contract. Before publishing or launching, it checks the updater namespace is empty/owned. Each fixture and candidate is beneath RUNNER_TEMP; no production game directories, downloads, or existing installed app are used. Background metadata refresh and auto-updating are disabled in test-owned settings.

The harness stops only the process it launched and deletes only a marker-verified GUID-named scratch root. The workflow outputs source SHA, published candidate executable SHA-256 and test result counts. It never shares raw user game paths or credentials. If the hosted UI cannot expose a native dialog or controls, the test fails rather than silently converting to an in-process unit test.

## Not yet established

The publish-from-source fixture is not signed/immutable release installation. Solid RAR/7z, real large Sifu/Stellar Blade mod sets, 500-mod performance, folder picker drag/drop, runtime game launch and injected crash/power-loss recovery remain distinct acceptance tasks (#791, #735, #793). No results may be claimed until exact-head hosted UIA job succeeds and all checks have terminal status.
