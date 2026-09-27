MHW MANUAL MOD MANAGER v7 — RESPONSIVE DESKTOP UI
======================================

WHAT CHANGED
------------
v7 keeps the v6 desktop workflow and focuses on responsiveness under large mod libraries/heavy file loads while preserving the transactional engine and State\V2 format.

Double-click:
  Manage Mods.bat
or:
  Launch Mod Manager.bat

The console should stay hidden and the MHW Mod Manager window will open.
Debug Console.bat remains available only for troubleshooting / advanced manual commands.

UPGRADE FROM v2/v3/v4/v5/v6
-------------------------
1. CLOSE Monster Hunter: World.
2. Make one backup copy of your current manager folder.
3. Keep your existing Mods folder and the ENTIRE State folder.
4. Copy the v7 files over the old manager files. Do NOT replace Mods or State with empty folders.
5. Run Test-Manager.ps1 once from Windows PowerShell before the first live deployment.
6. Open Manage Mods.bat.

The manager reuses State\V2, snapshots, backups, profiles, history, conflict winners, overlay relationships, and resource-provider rules.

UI WORKFLOW
-----------
Dashboard
  Quick counts for installed/enabled mods, blockers, and automatic overlaps.

Mods
  Search/filter the library. Select one or many mods and Enable/Disable Selected.
  Double-click a single row to toggle it.
  Changes are STAGED; nativePC is not touched until Apply safely.
  "Make selected mod win overlaps" explicitly makes that mod win every shared path it supplies.
  "Use as shared skin provider" pins the selected enabled mod for nativePC\pl\f_equip\mod_hepsy resources.

Conflicts
  Shows both blocking and automatically-managed overlaps.
  Harmless/known relationships need no action.
  For a blocker, choose a winner and either:
    - Use winner once: per-file decision only.
    - Remember as overlay: both mods stay ON and the chosen mod wins their overlapping files in the future.
    - Incompatible — keep selected: remembers incompatibility and stages the other mod OFF.
    - Clear saved rule: removes the remembered decision.

Outfit coverage
  Scans the bundled armor database and shows active/available/unmodded coverage per armor piece.

Profiles
  Save the staged/current setup, load a profile into staging, rename/delete/export/import profiles.
  Loading does not deploy until Apply safely.

Diagnostics
  Quick checks run automatically.
  Full health check verifies deployed files, captured sources, stored snapshots, original backups, layouts, and conflicts.
  Export Support Bundle creates a small ZIP containing state metadata, health/conflict summaries, recent history, UI draft, and recent logs. It DOES NOT include Mods or blob contents.

Activity & undo
  Shows transactional deployment history and can undo the latest completed deployment.

PERFORMANCE / RESPONSIVENESS
----------------------------
- Long-running capture/hash, Apply, Undo, health checks, outfit scans, support-bundle creation, and archive import/extraction run in background PowerShell runspaces instead of the WPF UI thread.
- A progress overlay keeps the window responsive. Read-only/capture/import work can be cancelled; transactional Apply/Undo cannot be interrupted mid-write and will commit or roll back safely.
- Conflict analysis builds one provider index for the enabled set instead of repeatedly scanning every enabled mod for every file path.
- Pair-level overlay/patch inference is memoized during each analysis pass.
- Multi-provider incompatibility checks scan the small saved-relation table instead of comparing every provider pair. This is especially important for mod_hepsy/shared-body resources used by many outfits.
- Search boxes are debounced and filter cached rows; typing no longer reruns conflict analysis.
- WPF DataGrids explicitly use row/column virtualization and recycling.
- State staging uses shallow immutable-map cloning instead of JSON round-tripping thousands of captured hashes for each toggle.
- UI drafts are compact deltas and are saved after a debounce instead of serializing the entire pending state on every click.
- Deployment change lists and transaction journals use append-efficient lists rather than repeatedly copying PowerShell arrays.
- Outfit coverage is lazy and no longer regenerates after every deployment.
- Recent history is cached between deployments instead of rereading history files during every visual refresh.
- Structured logs include elapsed milliseconds for conflict rebuilds and background operations, making performance regressions visible in support bundles.

ROBUSTNESS / AUTOMATION
-----------------------
- Automatically finds Monster Hunter World across common Steam libraries when possible.
- Remembers a selected game root for new installs.
- UI changes are staged and persisted to State\V2\ui-draft.json. A safe draft is restored after an app crash/restart only when the live manager state has not changed underneath it.
- Every deployment checks that MHW is closed.
- Live managed files are verified before deployment.
- File changes are journaled before they are written.
- Interrupted operations roll back; committed operations are finalized instead of incorrectly rolled back.
- Content-addressed snapshots let disabling/undo restore exact previous versions even if source folders changed later.
- Unmanaged files are backed up and ownership is released after restoration, so later manual edits become the next real base.
- Identical overlaps, texture overlaps, saved overlays, obvious base+option names, pinned shared providers, and high-confidence patch/fix subsets resolve automatically.
- Different structural model/material/physics files stay blocking unless a safe relationship is known or you teach one once.
- File-vs-directory collisions block before writes.
- State JSON is written atomically.
- Structured JSONL logs are written under State\V2\Logs.

SMART CONFLICT TYPES
--------------------
IDENTICAL          Same bytes; harmless.
SHARED-TEXTURE     Differing shared texture; one provider wins while all mods remain enabled.
TEXTURE-OVERRIDE   Differing texture; soft priority overlap.
PATCH-OVERLAY      Base + clearly named option/variant relationship.
PATCH-SUBSET       Small patch/fix whose files are a high-confidence subset of a related larger mod.
SHARED-PROVIDER    Pinned namespace provider wins.
OVERLAY-RULE       Relationship you explicitly taught once.
POSSIBLE-OVERLAY   Looks like a patch, but confidence is not high enough to guess; needs one decision.
HARD-STRUCTURAL    Different .mod3/.mrl3/.ctc/.ccl/.evbd/.evhl at same path; blocks until resolved.
HARD-GAME-DATA     Different game-data file at same path; blocks.
HARD-UNKNOWN       Different non-texture file type; blocks conservatively.
INCOMPATIBLE       Pair you marked as unable to coexist.

FILES / FOLDERS
---------------
Mods\                  Your named source mod folders. KEEP THIS.
State\                 Manager state/backups/snapshots/history/logs. KEEP THIS.
State\V2\Blobs\        Content-addressed captured files and originals.
State\V2\History\      Transaction history used for undo.
State\V2\Logs\         Structured diagnostic logs.
State\V2\ui-draft.json Staged UI changes, when any exist.
Modules\               Planner, deployment, profiles, import, reports, diagnostics.
MHW Mod Manager.ps1    Desktop WPF interface.
ModManager.ps1         Legacy/debug command-line interface.
Manage Mods.bat        Normal GUI launcher.
Debug Console.bat      Console fallback.
Test-Manager.ps1       Isolated temporary-game integration tests.
Armor Database.csv    Bundled MHW armor/model roster.

KNOWN LIMIT
-----------
The package is written for Windows PowerShell 5.1 / WPF and Monster Hunter: World on Windows. The development environment used to package this build is Linux, so the included Windows integration suite cannot be executed here. Run Test-Manager.ps1 on your PC before the first live v7 deployment.
