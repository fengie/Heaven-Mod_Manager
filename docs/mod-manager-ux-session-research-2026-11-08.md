# Mod manager command-surface and session-lifecycle audit — 2026-11-08

## Charter
- **Goal:** make useful actions discoverable without sideways scrolling; switch registered games in the same process; ensure a second launch activates the existing per-session UI; establish Windows CI + installed-client proof before shipping.
- **Scope / Object / Subject:** Windows WPF mod-manager interaction and process lifetime / existing UI actions and game-scoped runtime / nine XAML tabs, current Game-switch commands, App.OnStartup, AppServices, singleton/IPC options.
- **Type:** descriptive, with experimental CI failure investigation — the current implementation and its failures are known; a concrete change contract and regression strategy are required.
- **Questions:** Q1 which actions exist and what belongs in primary context? Q2 why are actions hidden? Q3 how to rebind a game without relaunching, preserving unfinished operations? Q4 how to hand duplicate launches to the original window without weakening process and user isolation? Q5 what evidence actually proves Heaven1 CI, release, and installed-client behavior?
- **Sources:** rank 1 current repository code/tests/merged PR history; rank 2 Microsoft WPF Application.ShutdownMode, System.Threading.Mutex, System.IO.Pipes.NamedPipeServerStream; rank 3 exact GitHub Actions job records and logs. No external UI design aesthetic blog is required.

## Canonical sources
1. Originating project: `src/MhwModManager.App/MainWindow.xaml`, `MainWindow.xaml.cs`, `App.xaml.cs`, `ViewModels/MainWindowViewModel.Games.cs`, `src/MhwModManager.Automation/AutoPopulateService.cs`, `AGENTS.md` on `main@446e933f778dae8f2da0238951e28b8b84f3f0b0`.
2. Microsoft: [ShutdownMode](https://learn.microsoft.com/dotnet/api/system.windows.application.shutdownmode?view=windowsdesktop-10.0), [main window](https://learn.microsoft.com/dotnet/desktop/wpf/windows/how-to-get-set-main-application-window), [named mutex](https://learn.microsoft.com/dotnet/api/system.threading.mutex?view=net-6.0), [named pipes](https://learn.microsoft.com/dotnet/api/system.io.pipes.namedpipeserverstream?view=net-10.0).
3. Native GitHub Actions: job labels `self-hosted`, `Windows`, `X64`, `mhw-mods`; runner names `heaven`, `heaven-v2`. These are remote execution proofs but **not** physical Heaven1 identity or installed Heaven2 acceptance.

## Findings ledger
| ID | Claim | Evidence | Rank | Answer |
| --- | --- | --- | --- | --- |
| F1 | Mods page puts seven filter chips in the only auto-horizontal-scroll command bar; bulk controls were at the far end | MainWindow.xaml Mods page (main) | 1 | Q1,Q2 |
| F2 | Nine tabs already expose actions; a separate generic action framework would duplicate functionality | MainWindow.xaml static Button Command survey | 1 | Q1 |
| F3 | The active game's database, scanner, planner, execution, catalog, watchers and VM are assembled in App.OnStartup | App.xaml.cs:74-180 | 1 | Q3 |
| F4 | Add Game / Switch Game / Configure Game all invoke RestartIntoGame, which launches the executable and shuts down the current process | MainWindowViewModel.Games.cs | 1 | Q3 |
| F5 | WPF OnMainWindowClose shuts down the process when that window closes; OnExplicitShutdown permits deliberate replacement | Microsoft ShutdownMode docs | 2 | Q3 |
| F6 | A named mutex is OS-scoped, with session/global namespaces; mutex names and user rights must be chosen deliberately | Microsoft Mutex docs | 2 | Q4 |
| F7 | Named pipes support same-machine duplex IPC; server must restrict access to the current user and accept a fixed bounded activation request only | Microsoft NamedPipeServerStream docs | 2 | Q4 |
| F8 | The first auto-intake Windows candidate failed analyzers CA1859/CA1869; subsequent compilation dependent projects could not proceed | Windows Workflow Feature PR Gate run 42456116530 | 3 | Q5 |
| F9 | A green AutoUpdate Contract is not a passing Windows feature gate, release publication, or installed-client result | PR #789 exact-head actions | 3 | Q5 |

## Scope filter
- Rejected a full-drive watcher: restrict source discovery to user-designated paths to avoid privacy, resource and live-game mutation risks.
- Rejected loading every command as top-level: primary/secondary placement follows task context; do not create a new global toolbar.
- Rejected interpreting a runner label as an installed Windows acceptance receipt.

## Object filter
- Rejected swapping only the active-game label: the WPF VM captures immutable per-game AppServices and must be reconstructed safely.
- Rejected firing a second executable at game switch: violates one-process requirement and creates a recovery race.
- Rejected a mutex-only duplicate-app guard: cannot notify or focus the already-running window.
- Rejected a broad network-accessible IPC endpoint: only same-user fixed `ACTIVATE` is allowed.

## Prioritized action map / implementation plan

| Surface | Primary visible actions | Context / secondary actions | Delivery |
| --- | --- | --- | --- |
| App bar | Active game selector + switch, Install Mod, Apply Changes, Launch Game | game settings | No horizontal scroll; guarded in-process switch |
| Mods | Search; All/Enabled/Pending/Updates/Problems/Needs Check/Superseded filters; **Auto Populate**, **Select All**, **Deselect All**, refresh | metadata sync, manual adoption, preview, discard | WrapPanel filters and fixed grouped actions |
| Dashboard | Launch, Apply, Import; Add/Scan/Remove auto-sort source | preview, recovery, diagnostics, backup/recipe, cleanup | Keep action tiles + reveal source status |
| Browse Mods | Search, Refresh Providers, Load More, Load Exact Files, Install | provider page, retry, installed-origin checks | Preserve progressive disclosure |
| Needs attention | Report crash, report GPU issue, inspect conflict | manual resolutions | Keep issue-local workflow |
| Profiles | Save, apply selected, refresh | collection export | Keep profile-local |
| History | Refresh, diagnostic capture, support bundle | detail logs | Keep history-local |
| Shared Files / Coverage | Explain why, refresh | filter detail | Keep local to content |
| Settings | updater, storage, legal/support links | storage reclaim (explicit) | Keep destructive/space-changing actions in Settings |

## Acceptance and verification
1. No horizontal scroll for command bars at 1040-, 1280-, and 1480-pixel windows; filters wrap, all primary actions remain visible and accessible by keyboard.
2. Select All stages the visible filtered mods only; Apply remains the separate deployment boundary.
3. Auto Populate includes the newly routed/processed Inbox before planning; errors and no-mod state explicitly explain what is missing.
4. Switching game keeps the same PID and main Window, safely stops the old game-scoped services, reconstructs the per-game session and rolls back the registry selection when an initialization fails.
5. Dirty/staged changes and active transactions block or require explicit confirmation; no concurrent updater ownership during switch.
6. Second launch (same user/session) must not open another WPF main window; request activates the running Window, including minimized/startup cases. Malformed/untrusted IPC requests are ignored.
7. Prove Windows compilation and whole-suite/regressions on the **exact PR head**, plus representative packaged-client game-switch and duplicate-launch smoke tests. Identify runner by recorded hostname, not runner label. Merge and release with ordinary safety gates. Verify installed Heaven2 separately.

## Risks and unresolved
- In-place reconstruction currently lives inside a long App.OnStartup method; extracting only the game-scoped lifetime seam must preserve migration, recovery, updater singleton ownership and diagnostics. Avoid directly reusing disposed services.
- File watcher events, updater checks, and async WPF callbacks can outlive a VM; cancel/await or guard switch to prevent stale callbacks and profile cross-contamination.
- Cross-session or cross-account duplicate launches do not share a visible desktop. Single-instance scope should be **same authenticated user + desktop session**, not machine-global.
- Exact runner hostname and physical Heaven1 proof require machine-specific CI receipt not yet available in current connector result. CI logs are not installed-runtime receipts.

## Implementation sequence
A. Finish already-open PR #789: responsive Mods command surface, game-auto-intake, no-op Auto Populate repair; exact Windows tests and diff review.
B. Separate lifecycle PR: same-session singleton via owner-restricted named mutex + pipe activation, with deterministic two-process tests.
C. In-process game switch: extract game session service creation from startup, safely replace app/VM scoped services, preserve window/main process, prove failure/retry/cancel tests.
D. Canonical Windows release gate, provenance, installed-client acceptance; never equate a merged PR with physical deployment.

**Continuity:** successors must re-read and recursively preserve `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, relevant Learned Rules and current state; no release claim before exact evidence.
