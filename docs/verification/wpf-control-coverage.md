# WPF controls and behavioral coverage

The [feature matrix](feature-test-matrix.json) describes service and integration coverage. The [control inventory](wpf-control-inventory.txt) lists the app's source sites, including all ten main tabs, eight Advanced Tools tabs, shared templates, dynamic dialogs, row controls, keyboard bindings, drag/drop handlers and native picker call sites. Each line identifies the source file, containing tab/template or method, control type and binding or handler. Repeated lines count independently. Grid columns and navigation/selection controls are included even when their only interaction is sorting, scrolling or selecting.

The [behavior table](wpf-control-behavior.tsv) maps individual sites to named installed WPF and in-process WPF test methods, fixture conditions, interaction and observable outcome. An absent mapping means no independently attributed installed outcome for that site in this inventory. A method reference identifies an executable test; a passing installed run must provide its own source revision, profile and result. Identically labelled actions on two surfaces need separate attribution. For example, the archive picker test proves the dialog imports a file but its window-wide lookup does not distinguish the two “Install Mod” buttons. Search text edits prove live filtering; pressing Enter is a different action.

The #940 source at `984919c6` has 387 control, event and supporting sites, including 118 XAML buttons, 103 bound command sites and two routed scroll commands. The 37-profile registry fixture spans 21 generic-root, 13 Unreal, two BepInEx and one MHW adapter. In-process tests open the real settings editor for each profile, check all six fields, invoke Save and compare the complete returned profile. A separate test opens a changed editor and cancels it. The FOMOD tests cover invalid and corrected required choices, checkbox selection through accessibility, disabled options and cancellation. These tests use isolated profiles and packages.

The behavior table maps only attributable sites. Its installed scenarios include a registry-wide fixture and narrower generic, Workshop and Unreal fixtures. Ten main tab mappings mean the hosted test selects each tab and observes its contents; they do not establish effects for every child control. The row staging test drives the real checkbox and verifies both enabled and denied states. The feature matrix also identifies service-level tests, including row composition and catalog presentation. In-game loading and installed host coverage require their own receipts.

| Source area at #940 | Inventoried sites | Sites with a specific behavior mapping |
| --- | ---: | ---: |
| Main shell and tab selection | 27 | 13 |
| Dashboard / Mods / Coverage | 76 | 9 |
| Profiles / History / Settings / Installed files | 44 | 7 |
| Browse Mods / Needs attention / Shared Files | 70 | 0 |
| Advanced Tools tabs and code-behind | 76 | 0 |
| Game settings editor / FOMOD dialog | 22 | 12 |
| Shared templates, code events, native pickers and other surfaces | 72 | 2 |

These counts include presentation and implementation sites. Each missing mapping needs a suitable scope. For Browse Mods seed exact provider responses before exercising search, conflict choices need two competing files and an expected winner, and Advanced Tools needs paired profiles, an upgrade or portable recipe according to the selected tab. Exercise these states once per distinct implementation and verify live applicability for every registered profile. Rerunning 37 identical provider, modal or OS-dialog scenarios solely to fill a table would add cost without testing a new code path.

## Applicability during a game pass

| Surface | Fixture or state needed | Observable result |
| --- | --- | --- |
| Game selector, library and inbox | Every discovered profile; one managed item and one pending change | Active profile stays correct, staged state survives refresh, preview preserves live bytes, apply and undo change only the expected files |
| Installed and externally owned rows | Current roots, stale roots, and Workshop ownership when present | Owned items have the expected staging controls; external rows retain their owner and source bytes |
| Coverage, conflicts and shared files | Adapter support, overlapping providers, a review-only item, one selected conflict | Correct capability and enablement state; the selected winner and target match the preview |
| Profiles, history and advanced workflows | Two saved setups, deployment history, conflict or rule, matching selection | Compare or edit the intended fixture; verify saved state after reopening and preserve unrelated files |
| Catalog, browser and updates | Known provider fixture, selectable file, empty/error variants | Search returns expected data; selection enables eligible actions; cancel preserves state; exercise updater handoff in the existing isolated updater tests |
| Settings, startup, FOMOD, pickers and confirmations | Corresponding dialog opened, optional/required FOMOD choices, editable and locked fields | Verify enabled/disabled polarity, positive outcome and cancel path; no writes to unrelated fixtures |

For each registered game, collect the rendered controls and enabled state once after switching in the same installed process. Then seed the smallest specific state for missing tabs, templates or modal choices. Associate each observation with its source site, profile, action, precondition and observed outcome. “Missing fixture”, “disabled”, “observed”, “invoked” and “effect verified” represent different evidence. Existing shared adapter/state fixtures can carry expensive outcome tests; record exactly which game actually ran. Avoid invoking updater, browser, maintenance, side-effecting dialogs or game launch against personal data merely to count a click.

## Updating this inventory

Run `scripts/Get-WpfControlInventory.ps1` under PowerShell 7 on Windows and inspect the difference against the checked-in inventory. Update the file and behavior mappings for the specific change. The ordinary integration test compares the full sorted output, including duplicate sites, and verifies referenced methods and compiled command owners. Source inspection cannot prove that a particular window supplies the expected runtime data context, that a lazy template materialized, or that a launch succeeded. Native operating-system dialog contents, dynamically loaded third-party code and UI outside Manager are beyond the source inventory.
