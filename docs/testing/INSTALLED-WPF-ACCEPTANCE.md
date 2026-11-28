# Installed WPF mod lifecycle acceptance

InstalledWpfModLifecycleE2ETests is an xUnit v3 suite using real Windows UI Automation on a runnable WPF application published from an exact source revision. It does not call view-model commands as a substitute for UI testing.

## Running the discovered-profile checks

From the repository root on Windows, run `dotnet test --project tests/MhwModManager.IntegrationTests -c Release --filter-class MhwModManager.IntegrationTests.DiscoveredGameLifecycleTests`. The default input is the checked-in, sanitized H2 inventory. For a live census set `MHW_DISCOVERED_GAME_REGISTRY` to the Manager home's `State` directory containing `games.json`, then run the same command. Other tests may temporarily change the process's Manager home. A dropped or duplicate registry entry fails discovery. Each case remaps executable, save, storage and deployment locations to a temporary directory.

The November 20 H2 snapshot contained 37 registrations. The read-only host census found 32 available executables, five missing executables, seven retained sibling roots, ten unmatched Steam folders, 945 registered Workshop entries, and 30 Workshop entries for an unregistered app. The tests exercise both available and missing installations and external ownership with disposable fixtures for every accepted profile. Online provider responses, account actions, and actual third-party game behavior need separate evidence. [Host inventory](https://github.com/fengie/Heaven-Mod_Manager/issues/939#issuecomment-7052456441)

## Functional tests

1. Create a GUID-named temporary manager home and two synthetic registered games with harmless executable placeholders (never launched), empty game Mods folders, and preserved sentinel bytes.
2. Import a one-mod ZIP through the app's native archive picker. Verify its entry appears disabled in the actual Mods list, source SHA-256 is unchanged and no live game files are written.
3. In a separate fixture, process the game Inbox using the actual Dashboard More tools UI. Verify the extracted package and visible checkbox.
4. Toggle the imported mod, Preview Mod Changes and prove the preview is read-only, Apply Changes and prove exact fake-game target bytes, then Undo and prove baseline restoration.
5. Select two ZIPs with a native multi-file picker; verify that confidently routed packages for the active game automatically appear as two disabled library rows without a separate Inbox command, preserving source hashes and fake game sentinel bytes.
6. Switch between the two registered fake games through the real WPF selector without restarting the process or altering any sentinel bytes.

The hosted acceptance workflow requires all configured cases to pass with zero skips and separate ordinary lifecycle and moved-install results for each registered profile. The per-profile test launches a disposable probe from the registered executable path. Read the job results and the per-game evidence file together.

## Isolation and evidence

The workflow runs only on disposable GitHub-hosted windows-2025 VMs, not on a self-hosted personal machine. Both workflow and test enforce a hosted-Windows opt-in contract. Before publishing or launching, it checks the updater namespace is empty/owned. Each fixture and candidate is beneath RUNNER_TEMP; no production game directories, downloads, or existing installed app are used. Background metadata refresh and auto-updating are disabled in test-owned settings.

The harness stops only the process it launched and deletes only a marker-verified GUID-named scratch root. The workflow outputs source SHA, published candidate executable SHA-256 and test result counts. It never shares raw user game paths or credentials. If the hosted UI cannot expose a native dialog or controls, the test fails rather than silently converting to an in-process unit test.

## Local acceptance scratch

Use `scripts/testing/Run-Tests.ps1` for standalone source verification. Its
test assemblies and build intermediates are created under a run-owned
artifacts directory and removed when the command returns. Pass
`-KeepBuildOutputs` when you need the normal project `bin` and `obj` directories
for a subsequent command. On Windows, the next standalone run can reclaim an
interrupted run's marked build directory after 24 hours and a verified restart
of the same host. A restart rules out surviving build and test workers.
Direct `dotnet` commands use the SDK's ordinary output locations.

For manual probes, dot-source `scripts/testing/AcceptanceScratch.ps1` and call
`New-MhwAcceptanceScratch`. Put generated installs, builds, extracted packages and
copied fixture state under the returned `Path`. Store reviewed scripts and compact
receipts separately. After the probe and its children exit, pass the returned
object to `Remove-MhwAcceptanceScratch` from `finally`.

`Test-UpdaterPackage.ps1` opts into stale collection because ZIP extraction and
verification run in the creating process. Pass `-ReclaimAfterProducerExit` only
when the run has no workers that could outlive that process. The normal test
runner can reclaim eligible scratch after 24 hours once the producer has exited.
For probes that start installers or other workers, the default keeps abandoned
scratch for review. Keep original inputs and any evidence you need outside it.

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
