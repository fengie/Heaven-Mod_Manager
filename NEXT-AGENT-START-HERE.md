# NEXT AGENT — START HERE

## Permanent recursive continuity obligation — read before any edit

You inherit the repository's **permanent continuity constitution**.

Before modifying this project, read:

- `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`
- `_AGENT_CONTEXT/LEARNED_RULES.md`
- `_AGENT_CONTEXT/CURRENT_REVISION.json`
- the remaining order in `_AGENT_CONTEXT/README_FIRST.md`

Preserve the constitution. Obey active Learned Rules. Add a Learned Rule only when a concrete, durable discovery justifies it.

Before finishing, update the repository handoff and explicitly require your **successor** to inherit, preserve, and recursively propagate these same rules. That successor must repeat the requirement again for the **agent after them**.

The next agent must be able to continue without previous chat history.

**Do not break the chain.**


## Current checkpoint: PlannerSnapshotRepository candidate awaiting exact Windows closure

PlannerSnapshotRepository initial source: `8e0068bd44cc6735ffa9478067923ad5d9c54506`.
Focused regression-test hardening: `64e666a19ce17c21bc696b46cce9c07bb257a686`.
Repair source after failed run `36335255922`: `528401925b1d09b3d65c9652de8e4f2024e3677f`.
The first hosted run exposed two missed callers and two LR-001 trace gaps; that failed run is preserved as superseded evidence. This candidate is not green until a fresh full hosted Windows Release Gate passes. Do not begin another source boundary while it is unresolved.

Governance closure remains exact commit `73f1298455ec4c651e211488ececf9803504e60d`, hosted Windows run `36333960215`; the later documentation closure `c9b27b98d280b144ba52ba35167f1fcb594945bd` also passed run `36334644325`.

Canonical architecture design base: `0129607a0558da6a596e1688a04f3051e5f6ce40`.

No production source changed in this checkpoint. The current decision is
**Path B: stop splitting MainWindowViewModel**.

Before editing, read:

- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md`
- `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`
- `_AGENT_CONTEXT/NEXT_STEPS.md`

The audit found no remaining page-model seam with lower coupling than the
alternatives. Mods/ModsView, Conflicts, IssueSuspects and Overlaps share
staged/effective/diagnosis state; import/Nexus/profile mutation/launch/crash/
health flows are application workflows; RunBusy/status/lifetime remain shell
responsibilities.

The exact next production boundary is **PlannerSnapshotRepository only**:
move the existing read-only `LoadPlannerSnapshotAsync` query assembly out of
ManagerDatabase, preserve current behavior and connection semantics, add
focused parity/planner tests, then run a fresh full Windows Release Gate.
Do not move `GetModsAsync` in the same checkpoint and do not touch
DeploymentExecutor transaction ownership.

The closed green production baseline is still exact commit
`106a4569b572473394aa075bcfa5d9c03f2fe44d`, run `36331057943`, because
this audit changed documentation only.

Preserve one source boundary at a time, exact SHA-bound evidence, and the
continuity protocol. Update the audits if implementation reveals a material
coupling assumption that was wrong.

**Do not break the chain.**


---

## Closed checkpoint: Games list presentation state

Hosted Windows Release Gate `36331057943` verified exact commit
`106a4569b572473394aa075bcfa5d9c03f2fe44d` (last production source `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`): **25/25**
verifier, **615/615** fingerprints, **6389** explicit call sites with **0**
uncovered, Core **79/79**, Automation **20/20**, Integration/fault injection
**66/66**, self-test **11/11**, App win-x64 analyzers PASS, and ReadyToRun
self-contained publish PASS.

Release SHA-256:
`4872ABDA6D548CB9F97668AF1A3019AC44B146A9A66876F92B065D7009455189`

Evidence/cache persistence commit:
`750a3232ad9ac82bd1587ddd903709b886c9b8bb`

The earlier run `36330808544` is superseded historical evidence. It reached
24/25 and correctly caught the missing `ScanInstalledGames` entry trace; the
final verified source includes that trace and a regression assertion.

Architecture boundary confirmed by this slice:

- `GamesPageViewModel` owns only registry `Load()` -> observable rows.
- `SelectedGame`, discovery, add/configure, `SetActive`, switching,
  process restart/WPF shutdown, `RunBusy`, and `StatusText` remain shell-owned.
- `AppPaths.Discover` still resolves the active game before startup rebuilds
  the game-scoped service graph.

Next: re-audit remaining `MainWindowViewModel` responsibilities before choosing
another source slice. Do not force `IssueSuspects`; if no clean presentation
seam remains, move to a separately scoped ManagerDatabase repository-extraction
design with explicit transaction-boundary mapping first.

## Closed checkpoint: Profiles read/list page view model

Hosted Windows Release Gate `36328183152` verified exact commit
`04bc05779f5d94fa3e2e8cc3bf80fbc6fbed09b8` (production source `19a1ad4a4e665e4ce7f38586dea5f08f1c3acdf0`): **25/25**
verifier, **613/613** fingerprints, Core **79/79**, Automation **20/20**,
Integration/fault injection **65/65**, self-test **11/11**, and ReadyToRun
win-x64 publish PASS.

Release SHA-256:
`A150FFA7B832C56535A9CA19DCFEDD7640C3BE1F8E9ACB4D40881CB8C69F93B8`

Evidence/cache persistence commit:
`122bcdb525bb432e73dff6f5887a63e065246964`

The next candidate should be **Games presentation/state only**, after inspecting
the current seam. Keep game switching, service/path rebuilding, application
lifetime, `RunBusy`, and global status ownership in the shell/composition root.

## Closed checkpoint: Coverage page view model

Hosted Windows Release Gate `36327634813` verified exact commit
`e3ed3be730000d1829b02e5d2d29b3f23ca52d94` (production source `855f6e5eb4998aa442538636b76f5c644146eb6a`): **25/25**
verifier, **611/611** fingerprints, Core **79/79**, Automation **20/20**,
Integration/fault injection **64/64**, self-test **11/11**, and ReadyToRun
win-x64 publish PASS.

Release SHA-256:
`40B47B6E3C9CF21A0945095FE28E118540B415FBD9177190BDB99FE80C9657A8`

Evidence/cache persistence commit:
`e62e7ad5d93cdb6c6ae3d6e8562d6fae667e9c02`

The next intended source boundary is **Profiles read/list state only**. Preserve
the existing `Profiles` and `RefreshProfilesCommand` binding surface. Keep
profile staging/save/delete actions, `RunBusy`, and global `StatusText` in
`MainWindowViewModel` for the next slice.

## Closed checkpoint: Activity page view model

Activity extraction is closed by hosted run `36325994246` at exact commit
`5eab48f0a2139e3aee96a7c71e4466d2e1168877` (production source `e2396c7c91c5d8d88fe229603689539b5cdfb2da`): **25/25** verifier,
**609/609** fingerprints, Core **79/79**, Automation **20/20**, Integration
**63/63**, self-test **11/11**, ReadyToRun publish PASS.

The next intended source boundary is **CoveragePageViewModel only**. Preserve
`OutfitRows` and `RefreshOutfitsCommand` bindings, and keep shell-global
`StatusText` ownership in `MainWindowViewModel`.

## Active candidate: Activity page view model

Run `36325764389` targets a malformed intermediate connector patch and is superseded; verify the corrected source SHA below instead.

Production source commit `e2396c7c91c5d8d88fe229603689539b5cdfb2da` extracts the Activity read/state seam
into `ActivityPageViewModel` while deliberately preserving
`ActivityRows` and `RefreshActivityCommand` bindings. It is not green until
a fresh Windows Release Gate passes. Do not start a second page extraction
before that closure.

## Current checkpoint: architecture / Explain Why is CLOSED

Hosted Windows Release Gate `36325133722` verified exact commit
`9717a22d3338f77e63cd409a80d2ec5fc3c924f2`; the last production-source edit is
`098d617bcb3dcdd044e3fdb8319ba506c97082af`. Verification is **25/25**, function fingerprints are
**607/607**, Core **79/79**, Automation **20/20**, Integration/fault injection
**62/62**, self-test **11/11**, and the ReadyToRun win-x64 publish passed.
Release SHA-256: `DF87A48716596ABFFF545DD6C73BAAE02954167424908850D943BFFA3833A2D6`.

The next source change must start a new verification boundary. The next intended
slice is one page-view-model extraction at a time, beginning with Activity.
## Immediate architecture-candidate verification update

Hosted run `36324750213` passed the exact repository verifier but the release
win-x64 analyzer gate found one CA1826 diagnostic in overlap path selection.
Production source commit `098d617bcb3dcdd044e3fdb8319ba506c97082af` contains the minimal analyzer-safe
fix. Treat it as unverified until a fresh full Windows Release Gate passes and
persists exact evidence.

Read `AGENTS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/README_FIRST.md`, and `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` **before modifying this project**.

## Canonical repository state

GitHub repository `fengie/mhw-mods` on `main` is the canonical development state. Inspect recent commits/diffs relevant to the task before editing. Source ZIPs remain useful release/export artifacts, but they are not the primary source of truth.

## Current revision

v8.8.0 has a closed hosted Windows baseline: commit
`5f6789af499fcc1afe6cb5d38244927bb02335fb`, GitHub Actions run
`36321128433`, **25/25** verification stages passed, all 602 exact production
function fingerprints promoted, and the win-x64 release publish passed. Read
`_AGENT_CONTEXT/EVIDENCE/v8.8.0-hosted-windows-closure.log`.

The post-v8.8 architecture / Explain Why milestone is closed by run `36325133722` at exact commit `9717a22d3338f77e63cd409a80d2ec5fc3c924f2`. Any subsequent production-source change starts a new verification boundary and must pass a fresh Windows gate.

## Mandatory continuity requirement

You are responsible for two deliverables:

1. the requested project work; and
2. a durable handoff of everything materially learned while doing that work.

Update `_AGENT_CONTEXT/` as you investigate and modify the project. Preserve architecture discoveries, execution/data flows, invariants, decisions and rationale, rejected approaches, regressions/root causes, verification evidence, uncertainties, changed files, debugging knowledge, and next steps.

Before finishing repository work:

- update `_AGENT_CONTEXT/` and `_AGENT_CONTEXT/CURRENT_REVISION.json` with material discoveries and the verification state that applies to the changed source;
- run `scripts/Test-AgentHandoff.ps1`;
- preserve `_AGENT_CONTEXT/handoff-manifest.json`, `CONTINUITY_PROTOCOL.md`, and `AGENTS.md`;
- commit the context/handoff updates with the code they describe;
- if producing a source ZIP, use `Build Source Handoff.bat` / `scripts/Build-Source-Handoff.ps1` when possible;
- explicitly tell the next agent to repeat this continuity practice.

The next agent must be able to continue without access to previous chat history.

**Do not break the chain.**

## Verification evidence

The canonical closed v8.8 evidence is
`_AGENT_CONTEXT/EVIDENCE/v8.8.0-hosted-windows-closure.log`. Earlier first and
second Windows logs remain useful historical evidence, but their 13/12 and 24/1
states are superseded by the final 25/25 hosted closure. For any later source
change, reuse cached stages/functions only when the verifier confirms exact input
fingerprints; never copy a green status manually.
