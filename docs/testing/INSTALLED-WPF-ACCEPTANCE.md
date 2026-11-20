# Installed WPF mod lifecycle acceptance

InstalledWpfModLifecycleE2ETests is an xUnit v3 suite using real Windows UI Automation on a runnable WPF application published from an exact source revision. It does not call view-model commands as a substitute for UI testing.

## Running the discovered-profile checks

From the repository root on Windows, run `dotnet test --project tests/MhwModManager.IntegrationTests -c Release --filter-class MhwModManager.IntegrationTests.DiscoveredGameLifecycleTests`. Manager's configured `MOD_MANAGER_HOME` or `MHW_MANAGER_HOME` supplies `State/games.json` when present. Set `MHW_DISCOVERED_GAME_REGISTRY` to select a different directory containing a registry. Clean CI uses the checked-in, sanitized H2 inventory. A dropped or duplicate registry entry fails discovery. Each case remaps executable, save, storage and deployment locations to a temporary directory.

The November 20 H2 snapshot contained 37 registrations. The read-only host census found 32 available executables, five missing executables, seven retained sibling roots, ten unmatched Steam folders, 945 registered Workshop entries, and 30 Workshop entries for an unregistered app. The tests exercise both available and missing installations and external ownership with disposable fixtures for every accepted profile. Online provider responses, account actions, and actual third-party game behavior need separate evidence. [Host inventory](https://github.com/fengie/Heaven-Mod_Manager/issues/939#issuecomment-7052456441)

## Functional tests

1. Create a GUID-named temporary manager home and two synthetic registered games with harmless executable placeholders (never launched), empty game Mods folders, and preserved sentinel bytes.
2. Import a one-mod ZIP through the app's native archive picker. Verify its entry appears disabled in the actual Mods list, source SHA-256 is unchanged and no live game files are written.
3. In a separate fixture, process the game Inbox using the actual Dashboard More tools UI. Verify the extracted package and visible checkbox.
4. Toggle the imported mod, Preview Mod Changes and prove the preview is read-only, Apply Changes and prove exact fake-game target bytes, then Undo and prove baseline restoration.
5. Select two ZIPs with a native multi-file picker; verify that confidently routed packages for the active game automatically appear as two disabled library rows without a separate Inbox command, preserving source hashes and fake game sentinel bytes.
6. Switch between the two registered fake games through the real WPF selector without restarting the process or altering any sentinel bytes.

The hosted acceptance workflow requires 13 passing cases, zero skips, and a recorded UI result for each profile in the registry. The per-profile test launches a disposable probe from the registered executable path. Read the job results and the per-game evidence file together.

## Isolation and evidence

The workflow runs only on disposable GitHub-hosted windows-2025 VMs, not on a self-hosted personal machine. Both workflow and test enforce a hosted-Windows opt-in contract. Before publishing or launching, it checks the updater namespace is empty/owned. Each fixture and candidate is beneath RUNNER_TEMP; no production game directories, downloads, or existing installed app are used. Background metadata refresh and auto-updating are disabled in test-owned settings.

The harness stops only the process it launched and deletes only a marker-verified GUID-named scratch root. The workflow outputs source SHA, published candidate executable SHA-256 and test result counts. It never shares raw user game paths or credentials. If the hosted UI cannot expose a native dialog or controls, the test fails rather than silently converting to an in-process unit test.

## Manual bulk intake behavior

For manual multi-archive, folder and single multi-mod-pack inputs, approved packages routed
to the **active game** now flow through the existing SmartInbox safety, duplicate archiving,
indexing and visible list refresh within the same progress operation. Unidentified,
contradictory and installer-choice packages remain uninstalled for review. Packages
routed to a **different registered game** remain queued in that game's Inbox; no foreign
game database is opened by this active-session change. Background folder-watch scans
continue to stage packages only. The hosted WPF check includes a regression
requiring that two selected archives appear as disabled mods without a separate Inbox
action; it also verifies original archive hashes and fake live game sentinels.

## Not yet established

The publish-from-source fixture is not signed/immutable release installation. Solid RAR/7z, real large Sifu/Stellar Blade mod sets, 500-mod performance, folder picker drag/drop, third-party game loading and injected crash/power-loss recovery remain distinct acceptance tasks (#791, #735, #793). No results may be claimed until exact-head hosted UIA job succeeds and all checks have terminal status.
