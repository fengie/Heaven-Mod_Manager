# Auto-updater C10 WPF handoff hardening — 2026-09-28

## Exact source
- Working branch: `agent/auto-updater-20260928`.
- C10 WPF integration inherited from `1fbdd0619cc4e7bee00d4eeb70de1d4d488166d4`.
- Hardened production/test source: `fdff9ed940b8801c1b17bedb6e6de523d7b807a6` (`Harden updater WPF handoff safety`).
- Environment: heaven / Windows x64 / .NET SDK 10.0.401.
- C11 packaging/publication work is separate and is not part of this checkpoint.

## Defects reproduced before repair
Five new focused cases failed against C10:
- all three updater-owned health flags were silently accepted when dangling;
- one updater-owned health flag could be consumed as another health flag's value;
- helper handoff copied only the executable, not an owned runtime dependency.

A separate lifetime review found a race between the ViewModel's last "not busy" check and helper launch:
a new `RunBusy` foreground operation could begin in that window.

## Repairs
- malformed updater-owned health argument tuples now fail closed with `InvalidDataException`;
- handoff prevalidates and copies the complete product-owned `UpdaterHelper/` file closure, preserving relative paths and verifying every copied file;
- `UpdateHandoffGate` atomically arbitrates foreground `RunBusy` work versus the final helper-launch/shutdown handoff;
- once handoff is armed, no new foreground operation can begin before helper launch; failed/deferred handoff disarms cleanly.

## Exact verification
After the final source edit:
- focused updater/handoff suite: **74/74 PASS**;
- full `MhwModManager.IntegrationTests`: **170/170 PASS**;
- strict whole-solution Release build with `-warnaserror`: **0 warnings / 0 errors**;
- `scripts/Test-AgentHandoff.ps1`: **PASS**;
- `git diff --check`: PASS before source checkpoint.

No `Verify-Release.ps1`, `Build-Release.ps1`, hosted Windows Release Gate, cache promotion,
immutable GitHub release, or disposable old→new packaged installation is claimed for C10.

## Next boundary
Complete C11 as a separate verified boundary:
1. deterministic minimal updater package + full helper invocation closure;
2. exact updater metadata generation;
3. immutable private GitHub Release publication;
4. release-gate publication-negative/stale/evidence-only tests;
5. disposable real old→new and rollback closure.

Preserve the permanent continuity constitution and active Learned Rules. The successor must
recursively pass the same obligation to the agent after them. Do not break the chain.
