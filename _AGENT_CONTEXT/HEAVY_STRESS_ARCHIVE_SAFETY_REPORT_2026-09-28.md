# Heavy stress archive-safety report — 2026-09-28

## Scope and canonical truth

Canonical base when this pass began: `origin/main` at `2d6969c7d94438dd64a540dab93fc9a910a8458b`.
Work branch: `agent/heavy-stress-safety-20260928`.

This pass first audited the current tests and open safety branches instead of duplicating work.
PR #27 already owns recursive source-reparse hardening for ModScanner, unmanaged adoption, and Smart Inbox.
PR #29 had independently documented an archive destination-root physical-containment defect.
PR #30 separately documents archive cancellation/resource-budget defects. Those remain a different boundary.

The selected implementation boundary here is only archive extraction physical-root containment:
an archive/import operation must never gain write authority outside the caller-owned trusted Mods root.

All destructive fixtures used isolated temporary directories on `heaven2`.
No real game installation, mod library, Desktop, OneDrive content, or personal data was used.

## Existing coverage

The repository has meaningful layered coverage:
- Core/unit tests cover planner/conflict rules, path rules, compatibility, game profiles, logical families, and content classification.
- Automation tests cover backup, update diffing, duplicate analysis, metadata/category workflows, diagnostics, and Smart Inbox happy paths.
- Integration/fault-injection covers deployment journal/recovery, rollback, CAS integrity, archive traversal, SQLite state, and function-verifier behavior.
- Windows-specific tests already exercise real directory junctions for live DeploymentExecutor containment and scripted native replacement postconditions.
- Crash injection covers several before/after-commit deployment stages and interrupted rollback followed by restart recovery.
- CAS tests cover corruption, restore verification, concurrent capture convergence, cancellation cleanup, and recovery material.
- Existing archive coverage proved lexical `..` traversal rejection, but did not prove the caller's physical trusted root could not be redirected.

At baseline, the repository had 157 `Fact`/`Theory` declarations across Core, Automation, and Integration test projects.
The main hardening class already included deployment Add/Replace/Remove junction tests, startup-recovery junction tests,
ReplaceFileW 1175/1176/1177 state assertions, rollback interruption, source-edit detection, and archive parent-traversal rejection.

Important remaining broad gaps observed during the audit include:
- restartable migration interruption/idempotency and statement-level SQLite fault injection;
- wider concurrent install/uninstall/enable/disable mutation races;
- archive streaming cancellation and actual-output/free-space resource budgets;
- CAS filesystem identity/hardlink/digest-namespace policy;
- large-scale planner/scanner/reconciliation workloads;
- path-check/open TOCTOU windows that require stronger handle/identity semantics to eliminate fully.

## Gap discovered

`ArchiveInspector.ExtractSafely` originally treated the extraction destination itself as the reparse-check stop point.
`ArchiveImportService` extracts to `<modsRoot>\<name>.importing`, and Smart Inbox archive import extracts under `modsRoot`.
If `modsRoot` was a real Windows junction to an external directory, the destination did not yet exist,
so extraction created it through the junction and wrote archive bytes outside the intended managed library.

The existing descendant check also called `Directory.CreateDirectory(parent)` before inspecting that path for reparses.
With a descendant junction already present inside the destination, extraction could therefore create a new directory
in the external target and only then throw after noticing the junction.
## Tests added

### Archive_extraction_rejects_junction_ancestor_above_destination

Failure simulated:
- a real Windows directory junction at the trusted Mods root redirects to an external temp directory;
- extraction targets a non-existent child staging directory beneath that junction.

Invariant protected:
- archive extraction must not write outside the explicit trusted root through a reparse-point anchor.

Pre-fix evidence:
- exact focused run failed 0/1 because no `InvalidDataException` was thrown;
- the external redirected path was reachable by the old implementation.

Expected/post-fix behavior:
- reject before creating the extraction destination or writing an entry;
- external payload path remains absent.

### Archive_extraction_rejects_descendant_junction_before_creating_external_parent

Failure simulated:
- destination is physically under a normal trusted root;
- an existing descendant junction redirects one archive subdirectory to an external temp directory;
- the archive entry requires creation of another directory below that redirected path.

Invariant protected:
- safety validation must happen before filesystem creation, not after a side effect has already escaped.

Pre-fix evidence against the first trusted-root repair candidate:
- extraction threw, but the test still failed because `external\created` already existed;
- this proved the previous guard was fail-late rather than fail-closed.

Expected/post-fix behavior:
- reject on the junction before creating `external\created`;
- no external directory or payload file is created.
## Production repair

`ArchiveInspector` now requires an explicit `trustedRoot` for extraction.
It rejects destinations lexically outside that root and requires the trusted root to already exist.

Directory preparation is now component-by-component under the trusted root:
- inspect the trusted root itself for `FileAttributes.ReparsePoint`;
- walk each path component in order;
- reject an existing reparse component before descending;
- create only the next verified component;
- re-check the created component;
- only then open/write the archive entry.

`ArchiveImportService` and Smart Inbox archive import both pass their configured `modsRoot` as the trusted root.
The existing archive lexical traversal/no-overwrite protections remain in place.

This is still a path-based guard. A hostile process that swaps topology after the final check and before an open/create
remains a TOCTOU risk; this checkpoint does not claim handle-relative/file-ID-atomic containment.

## Bugs discovered

1. Trusted-root junction escape
   - Trigger: configured `modsRoot` is a Windows junction/reparse point.
   - Potential user impact: a mod archive can cause files to be created outside the managed mod library.
   - Regression: `Archive_extraction_rejects_junction_ancestor_above_destination`.
   - Minimal fix: explicit trusted root plus root-inclusive physical reparse validation.

2. Descendant fail-late directory creation
   - Trigger: an archive entry's parent path crosses an existing descendant junction.
   - Potential user impact: external directories can be created even though extraction ultimately reports failure.
   - Regression: `Archive_extraction_rejects_descendant_junction_before_creating_external_parent`.
   - Minimal fix: validate/create one directory component at a time before any deeper mutation.
## Verification executed so far

Real Windows `heaven2`:
- ancestor-junction regression before fix: **1 total / 1 failed** as expected;
- ancestor-junction regression after repair: **1/1 PASS**;
- descendant fail-late regression before ordering fix: **1 total / 1 failed** as expected;
- descendant regression after ordering fix: **1/1 PASS**;
- existing archive `../` traversal regression: **1/1 PASS**;
- HardeningTests: **30/30 PASS**;
- Automation tests: **20/20 PASS**;
- Integration/fault-injection: **91/91 PASS**;
- affected Automation test project build after restore: **0 warnings / 0 errors**.

At this intermediate report checkpoint the full repository verifier and release build were still pending.
They were subsequently executed successfully after one LR-001 instrumentation repair; see **Final local Windows verification** below.

## Remaining risks by evidence level

Proven safe by executed tests:
- trusted Mods-root directory junction is rejected before external payload creation;
- descendant junction is rejected before creating a deeper external parent;
- legacy archive parent traversal remains rejected;
- current Hardening, Automation, and Integration suites remain green.

Partially tested:
- ordinary archive import and Smart Inbox compile against the new trusted-root API,
  but this pass has not yet added a full service-level junction fixture for each caller.
- reparse policy is proven with directory junctions; a privilege-backed file-symlink leaf fixture is not part of this boundary.

Reasoned about but not experimentally eliminated:
- path-check/open topology-swap TOCTOU;
- hardlinks, which are not reparses;
- trusted-root ancestors above `modsRoot` are treated as caller/environment trust and are not walked to the volume root.

Blocked/separate:
- archive streaming cancellation/resource-budget defect from PR #30;
- recursive source traversal candidate in PR #27 and its hosted-gate requirement;
- migration interruption/idempotency, broad mutation races, and large-scale workloads.

Successors must preserve the permanent continuity constitution and the short-interval checkpoint/push rule,
and must require their successor to propagate both obligations to the agent after them.

## Final local Windows verification

Exact production/test source verified: `ea5c9f8ce121d58806fe503c1330dbce9e63d3ed`.

The first full `Verify-Release.ps1` attempt intentionally remained failed evidence:
- **24/25 PASS**, with the only failure being function fingerprint verification;
- the changed `ExtractSafelyAsync(string,string,string,CancellationToken)` wrapper had one LR-001 entry-trace gap and two uncovered call sites;
- all compile/analyzer stages, Automation **20/20**, Integration **91/91**, and self-test **11/11** were already green;
- no failed-run cache state was committed.

After adding the required `MasterDebugLog.BeginMethod()` scope, the exact verifier rerun on `heaven2` passed:
- repository verifier: **25/25 PASS**;
- function inventory: **613/613 verified**;
- explicit call sites: **6509**, uncovered **0**;
- trace gaps **0**, parse errors **0**;
- Core **79/79**, Automation **20/20**, Integration/fault injection **91/91**;
- automation self-test **11/11**;
- strict whole-solution/project analyzers: PASS.

`Build-Release.ps1` then passed on the same source:
- function scan **613 known-good / 0 pending**;
- win-x64 compile/analyzers PASS;
- self-contained ReadyToRun publish PASS;
- release ZIP SHA-256: `75EC78E6297510622B1FF6931F774273478292F53C7E073BBB83228A6C493027`.

The normal verifier/release scripts promoted the exact function/stage cache; no verification cache was manually edited or promoted.
Hosted Windows Release Gate evidence is still pending for this branch and must remain distinct from this local `heaven2` closure.
