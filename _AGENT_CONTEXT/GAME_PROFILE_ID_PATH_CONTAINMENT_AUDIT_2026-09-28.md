# Game-profile ID path-containment audit — 2026-09-28

## Status

**Documentation-only support checkpoint. No production source, tests, verification caches, or historical verification claims are changed by this audit.**

Canonical repository: `fengie/mhw-mods`  
Canonical commit inspected: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`  
Support branch: `agent/support-game-profile-id-containment-audit-20260928`

## Why this was selected

Parallel work already owns the automatic-updater publication boundary, frontend/UI work, agent-control-plane work, archive extraction hardening, remote-preview/network hardening, diagnostics export privacy, CAS hardening, migration hardlink behavior, and multi-instance mutation. The persisted game-profile identity -> per-game workspace/state-path boundary had no active branch or dedicated audit found in the inspected branch/PR inventory.

This boundary has high leverage because `GameProfile.Id` is persisted in global manager state and is later used as a directory component for per-game manager-owned data.

## Internal assignment

Audit only the trust transition from persisted game-profile identity to manager-owned workspace/state paths.

Inspect:

- `src/MhwModManager.Core/GameProfiles.cs`;
- `src/MhwModManager.Filesystem/GameProfileRegistry.cs`;
- `src/MhwModManager.App/App.xaml.cs` / `AppPaths.Discover`;
- existing game-profile and multi-game regressions;
- relevant filesystem-safety, portability, continuity, and Learned Rule documents.

Answer:

1. Can a persisted profile ID cause manager-owned paths to resolve outside the intended tool/state roots?
2. Do normal creation paths prevent this?
3. Does registry load/upsert independently validate persisted IDs before they become path components?
4. What regression contract should precede any production repair?
5. Can the issue be repaired without silently changing game/workspace identity?

Exclusions:

- no updater changes;
- no frontend redesign;
- no game-root reparse redesign;
- no deployment/CAS changes;
- no backup/restore implementation;
- no broad game-profile schema migration;
- no verification-cache promotion.

Success means leaving a source-backed finding, exact test plan, narrow repair options, verification limits, and successor handoff without modifying production behavior.

## Existing strengths

1. **Normal generic profile creation normalizes IDs.** `GameProfile.Generic` calls `NormalizeId(id)`, and registry-created generic profiles derive their ID from the display name through `GameProfile.NormalizeId`.
2. **Executable and mod-root relative paths are validated separately.** `GameProfile.NormalizeRelative` rejects rooted paths, traversal segments, colons, invalid filename characters, and unsafe trailing dot/space segments.
3. **Registry usability validation checks executable/live-root lexical containment under the configured game root.** This is useful even though it does not validate the profile ID.
4. **A path-safe identity primitive already exists conceptually.** `GameProfile.StorageKey` combines `NormalizeId(Id)` with a hash of ID + full game root. The current `AppPaths` workspace derivation does not use it.
5. **Existing tests cover normal arbitrary-game registration and unsafe mod-root traversal.** This narrows the missing regression to persisted identity rather than the already-tested relative mod-root path.

## Confirmed finding

### P1 — persisted `GameProfile.Id` is trusted as a workspace/state path component without validation

**Classification:** confirmed by static source inspection. Not runtime-reproduced in this support environment. No remote exploit path is claimed.

The trust chain is:

1. `GameProfileRegistry.Load()` deserializes `State/games.json`.
2. Loaded profiles are filtered through `GameProfileRegistry.IsUsable(profile)`.
3. `IsUsable` checks that `Id` is non-empty, but does **not** require it to equal a canonical normalized ID, reject separators/traversal/rooted forms, or otherwise prove it is one safe path segment.
4. `GetActive()` can return that loaded persisted profile.
5. For non-MHW profiles, `AppPaths.Discover()` uses the raw persisted `active.Id` in:
   - `Path.Combine(tool, "Games", active.Id)`;
   - `Path.Combine(state, "Games", active.Id, "Next")`.
6. Startup then creates manager-owned `Mods`, `Inbox`, `Mods Archive`, and `Next` directories and derives `manager.db` / `Blobs` beneath those computed paths.

Therefore a malformed persisted ID containing traversal or a rooted path can make the derived workspace/state path resolve outside the intended `<tool>/Games` and `<tool>/State/Games` roots before manager-owned directories/state are created.

Examples that a regression fixture should exercise include:

- `..\\..\\escaped`;
- an absolute Windows path;
- a value containing `/` or `\\`;
- `.` / `..`-style segments;
- IDs that normalize to a different value than persisted.

This is a **persisted-state validation / filesystem-containment defect**. Ordinary UI-created generic profiles are materially safer because their IDs pass through `NormalizeId`; the defect is reached through malformed/corrupted/tampered/legacy persisted registry content rather than the normal creation path.

### Impact

A malformed active profile can redirect manager-owned per-game state and workspace I/O outside the configured manager roots. At minimum this can cause unintended directory/database/blob creation or state association in another location. Subsequent services receive the escaped `AppPaths` roots and may perform ordinary manager operations there.

This audit does **not** claim arbitrary remote code execution, privilege escalation, or a demonstrated external attacker path. The severity is driven by violation of the project's fail-closed filesystem ownership/containment contract and the possibility of writes/state creation outside the intended manager roots.

## Secondary robustness observation

`GameProfile.NormalizeId` emits only lowercase alphanumeric/hyphen content, which prevents traversal syntax in normal creation, but it does not itself define a complete Windows-directory-name contract (for example reserved device basenames or a maximum identifier length). That is a separate P2 robustness question and should not be bundled into the first containment repair unless a focused Windows fixture proves a concrete failure.

## Existing test gap

### `GameProfileTests`

Current tests prove:

- generic executable/mod-root mapping;
- rejection of relative-path traversal for mod roots;
- common mod-root acceptance;
- conflict behavior.

They do not test persisted profile ID validity.

### `MultiGameTests`

Current integration coverage proves normal registry creation/activation and multi-game scanning, but it does not seed a hostile/corrupt `games.json` and prove the registry/startup layer fails before any escaped workspace/state side effect.

## Regression-first contract

The first implementation checkpoint should add a fixture around the persisted registry boundary, not merely another `NormalizeId` unit test.

### Required regression: malformed persisted ID is rejected before path creation

Arrange:

1. create a temporary manager tool/state root;
2. create a real temporary game root plus executable so all non-ID usability checks can pass;
3. write `State/games.json` directly with a profile whose `Id` is `..\\..\\escaped` (and repeat for rooted/separator cases);
4. mark that ID active when the fixture exercises active selection;
5. place a sentinel outside the intended `Games` / `State/Games` roots.

Assert:

- the malformed profile is rejected or startup fails closed before workspace creation;
- no `Mods`, `Inbox`, `Mods Archive`, `Next`, `manager.db`, or `Blobs` path is created outside the intended roots;
- the external sentinel is unchanged;
- no existing valid profile is silently rebound to a different workspace;
- the registry is not silently rewritten merely by reading it.

### Required unit contract: profile ID canonicality

Pin the chosen compatibility policy explicitly. A safe default is:

- persisted IDs must be a single canonical manager ID;
- validation is side-effect free;
- invalid persisted IDs are rejected, not silently normalized.

Do not make tests accept silent normalization until the collision/migration implications are deliberately designed.

## Repair guidance

### Preferred narrow first repair

Introduce one canonical ID validator in the Core/profile boundary and require `GameProfileRegistry.IsUsable` / `Upsert` to enforce it.

A conservative contract is:

- non-empty;
- exactly one path segment;
- no rooted form or directory separators;
- no `.` / `..`;
- equals the canonical normalized form expected for manager IDs;
- explicit Windows-name/length handling if the Windows fixture demonstrates that need.

The crucial property is that a persisted invalid ID never reaches `AppPaths.Discover` as an authority-bearing path component.

### Defense in depth

`AppPaths.Discover` should still prove that any derived non-MHW workspace and next-state root remain lexically under their intended parents before creating directories. This protects against a future caller bypassing registry validation.

### Do not silently normalize malformed persisted IDs during load

Silently rewriting `foo/bar` to `foo-bar` can:

- alias two distinct malformed IDs;
- reconnect the wrong per-game workspace;
- orphan existing state;
- make corruption look like a valid identity migration.

If legacy noncanonical IDs must be supported, design an explicit migration/relink checkpoint that proves old and new workspace ownership and collision behavior.

### Do not switch existing workspace derivation to `StorageKey` casually

`StorageKey` is attractive because it is normalized and root-sensitive, but replacing `active.Id` with it would change the on-disk workspace location for every existing generic game. That is a migration, not a one-line containment fix.

## Things deliberately not changed

- production C#;
- test code;
- game-profile JSON schema;
- game workspace naming;
- updater code;
- UI;
- deployment/CAS/recovery behavior;
- verification caches;
- existing historical verification claims.

## Verification actually performed

- inspected canonical GitHub `main` at `a8b581176aac0e6bcf09c049285ed40f4b2b392c`;
- inspected recent commits, open PRs, and support branches to avoid duplicate ownership;
- read repository startup/continuity documents and relevant company safety/verification/multi-agent doctrine;
- inspected actual source bodies for `GameProfile`, `GameProfileRegistry`, and `AppPaths.Discover`;
- inspected actual game-profile/multi-game test assertions;
- cross-checked the existing Windows filesystem and state portability audits;
- confirmed LR-004 already carries the general containment lesson, so no duplicate Learned Rule is added.

## Not verified

- no Windows runtime fixture was executed;
- no malformed `games.json` was launched through the WPF app;
- no .NET build/test command was executed from this GitHub-only support environment;
- no PowerShell handoff/release verifier was executed;
- no production fix was implemented or verified.

A successor must not convert this static finding into a runtime-reproduced claim without running the fixture.

## Unresolved questions

1. Should malformed persisted IDs be rejected with a dedicated recovery UI, or should the registry omit them and let the existing profile-selection path recover?
2. Are any historical real-world registries known to contain noncanonical IDs created before `NormalizeId` became universal?
3. Should Windows reserved-name/length policy be part of the same validator or a separate robustness checkpoint?
4. Does a future portable-state restore feature need an explicit identity-migration map for legacy IDs?

## Recommended independent future checkpoint

**Regression-first persisted game-profile ID containment.**

Implement only:

1. hostile/corrupt registry fixtures;
2. canonical persisted-ID validation;
3. defense-in-depth workspace/next-root lexical containment before first directory creation;
4. focused Core/Integration tests;
5. exact Windows repository verification/build gate.

Keep this separate from physical `GameRoot` reparse containment, workspace migration to `StorageKey`, backup/restore design, updater work, UI work, and multi-instance locking.

## Parallel-agent integration notes

- PR #58 / `agent/auto-updater-postupload-publication-20260928`: updater publication; no overlap.
- PR #55 / `ui/frontend-responsive-polish-20260928`: frontend/UI; no overlap.
- PR #47 / `feature/agent-control-panel-20260928`: agent control panel; no overlap.
- Existing support branches already cover updater, remote-preview, diagnostics, CAS, archive, migration-hardlink, and multi-instance boundaries.
- `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` remains the specialized authority for physical `GameRoot`/reparse semantics. This audit is narrower: persisted profile **ID -> manager workspace/state path** authority.
- LR-004 and LR-010 remain authoritative general containment rules; no duplicate rule was added.

## Successor handoff

Start from current canonical `main`, not this audit's base SHA if main has advanced. Read `AGENTS.md`, the permanent continuity constitution, active Learned Rules, this audit, and current parallel PRs before editing.

If implementing the repair, preserve exact profile/workspace identity semantics, write the hostile persisted-state regression first, and prove absence of escaped side effects before calling the defect closed. Run only the verification gates you can honestly report.

The successor must preserve this permanent continuity system and explicitly require their successor to recursively propagate it again.

**Do not break the chain.**

## Implementation checkpoint — 2026-09-28

Status: **implemented and locally release-verified; integration/hosted exact-main closure pending**.

The narrow repair was implemented without introducing a StorageKey migration: `GameProfile.IsCanonicalId` defines the exact accepted manager-ID form; registry load/upsert rejects invalid IDs; and `AppPaths.Discover` performs an independent lexical containment check for generic workspace/state roots before directory creation. Hostile persisted registry tests prove no escaped Mods/Next side effects and no registry rewrite-on-read.

Exact verified source: `e5eb300223c01800e3a7652ce4ffe303cbc168c5`. Focused tests passed 18/18 and 12/12. Full local Windows release verification passed 25/25, functions 737/737, Core 88/88, Automation 28/28, Integration 184/184, self-test 11/11, Build-Release, updater build 324, ZIP SHA-256 `75C0FD14ED8225AB37B5120243E61AA883674E969E8EED810A6257342A60DCF7`.

No new Learned Rule was added: the repair is a concrete application of existing fail-before-mutation/path-authority containment rules (including LR-004/LR-010), so duplicating doctrine would add noise.

## v8.8.4 reconciliation closure - 2026-09-28

Status: **implemented and locally release-verified on the current combined tree; integration/hosted exact-main closure pending**.

Canonical main advanced during implementation to v8.8.3 archive streaming cleanup. The profile-containment lane was therefore reconciled on top of main `8702934aabd6fb73bb95d29068e00169b35c2170`, release identity was moved to v8.8.4, and all focused/full gates were rerun instead of reusing pre-reconciliation evidence.

Exact verified product source: `5d5b52a2193f8b6c377dfc9da5f504b7ad4ffc33`. Focused profile tests 18/18 and 12/12; Verify-Release 25/25; FunctionVerifier 739/739 with 7,871/0 call-site coverage; Core 88/88; Automation 29/29; Integration 187/187; self-test 11/11; Build-Release PASS; updater build 331; ZIP SHA-256 `62113ACCC65212B6FFDD2917FB95CFD96C058035798047541EA16DF51282E914`.

No new Learned Rule is needed: this remains a concrete application of existing fail-before-mutation/path-authority containment doctrine.
