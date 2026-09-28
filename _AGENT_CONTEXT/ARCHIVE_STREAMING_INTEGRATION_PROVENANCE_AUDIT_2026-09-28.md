# Archive streaming integration provenance audit — 2026-09-28

## Canonical state inspected

- Repository: `fengie/mhw-mods`.
- Canonical branch: `main`.
- Canonical HEAD at the start of this audit: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.
- That commit is merge PR #57, **Integrate archive streaming cancellation and output budgeting**.
- PR #57 integrated head: `fd8b48fc92e6f5e64591fd1938b7ccce5ac94083`, based on `8d5cc311ed13f5cb7f0df1f9e7c3e8bf9fcaec82`.
- Active parallel production PRs at selection time were updater publication (#58), frontend UX (#55), and the engineering control plane (#59). This audit deliberately avoids those production boundaries.

## Selected support assignment

Audit and repair the repository's durable truth immediately after archive streaming/resource-budget integration.

Questions:

1. Is the archive streaming implementation actually present on canonical `main`?
2. Do the regression tests described by PR #57 actually exist on `main`?
3. What verification evidence exists for the integration head?
4. What verification evidence exists for the exact canonical merge commit?
5. Do startup/next-step documents distinguish those facts correctly?
6. Can continuity be repaired without touching production code or overlapping active updater/UI/control-plane work?

Exclusions:

- no `ArchiveInspector` behavior changes;
- no updater/UI/control-plane changes;
- no verification-cache promotion;
- no historical evidence rewriting;
- no claim that a local integration-head run is an exact-main hosted run.

## Confirmed repository behavior

Canonical `src/MhwModManager.Filesystem/ArchiveInspector.cs` now contains the streamed async extraction implementation:

- archive entry streams are opened with the caller cancellation token;
- copy proceeds through an explicit async read/write loop;
- cumulative actual output bytes are checked before each write;
- cancellation and actual-output-budget failures remove the currently owned partial output file;
- the synchronous compatibility entry point delegates to the async implementation.

Canonical `tests/MhwModManager.IntegrationTests/HardeningTests.cs` contains regressions that:

- cancel during one large entry and require an `OperationCanceledException`;
- require the partial output file to be removed after cancellation;
- enforce a configured actual-output budget;
- require the partial output file to be removed after the budget failure.

Therefore the old continuity statement that archive streaming cancellation/resource budgeting is merely a future implementation candidate is stale.

## Verification provenance

### Evidence that exists

PR #57 records a fresh Windows/.NET verification run on its integration head `fd8b48fc92e6f5e64591fd1938b7ccce5ac94083` after rebasing the work onto then-current base `8d5cc311ed13f5cb7f0df1f9e7c3e8bf9fcaec82`.

The PR records:

- `Verify-Release.ps1`: **25/25 PASS**;
- function verifier: **728/728** functions;
- explicit call sites: **7,772 / 0 uncovered**;
- Core: **79/79**;
- Automation: **24/24**;
- Integration/fault injection: **177/177**;
- self-test: **11/11**;
- strict analyzer/build gates: **0 warnings / 0 errors**;
- `Build-Release.ps1`: PASS;
- win-x64 ReadyToRun and updater-helper packaging: PASS;
- release artifact SHA-256: `AC3571853650CFA91243199B23A44007488F9244780FCBD18A7A38552B652734`.

This audit did **not** rerun those Windows commands. It treats the PR record as existing repository evidence, not as a newly reproduced result.

### Evidence that does not yet exist

At audit time, GitHub reported no workflow runs and no combined commit statuses for exact canonical merge commit `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.

The integration-head verification is strong evidence for the source/test change, but it must not be silently relabeled as an exact-main hosted gate for the merge commit. The repository's existing `lastClosedVerificationCommit` must remain bound to its earlier exact verified input until a normal verifier/gate produces newer evidence.

A later exact-main (or exact descendant with unchanged archive source) Windows Release Gate can close this provenance gap. The active updater work is based on this newer main lineage, so its eventual full hosted gate may provide that closure if the archive source remains unchanged and the evidence is recorded precisely.

## Finding: P2 coordination / verification-provenance drift

The source implementation and tests landed before the startup/next-step documents were advanced. As a result, a new support agent following `README_FIRST.md` / `NEXT_STEPS.md` could incorrectly select archive streaming as unimplemented work and duplicate an already-merged production boundary.

This is not a newly discovered production defect in archive extraction. It is a repository-coordination defect with two risks:

1. duplicate or conflicting production work in a highly concurrent repository;
2. accidental promotion of local integration-head evidence into an exact-main hosted claim.

## Continuity repair

This branch updates the newest-status sections of:

- `_AGENT_CONTEXT/README_FIRST.md`;
- `_AGENT_CONTEXT/NEXT_STEPS.md`;
- `_AGENT_CONTEXT/ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md`.

The repair states:

- archive streaming cancellation / actual-output budgeting is implemented on canonical main;
- PR #57's integration head has the recorded full local Windows release verification;
- the exact canonical merge commit did not yet have a hosted workflow/status result at this audit checkpoint;
- the active updater boundary remains separate;
- no verification cache or last-closed exact evidence is promoted by this documentation-only support work.

No new Learned Rule is added. Exact-input verification provenance and recursive continuity are already represented by the active rule/doctrine system; duplicating them would weaken the ledger.

## Verification actually performed by this support agent

- fetched canonical `main` and confirmed exact HEAD `a8b581176aac0e6bcf09c049285ed40f4b2b392c`;
- inspected recent main history and open PRs;
- inspected PR #57 metadata and unified diff;
- inspected the current `ArchiveInspector` source body on `main`;
- inspected the current archive cancellation/output-budget integration tests on `main`;
- fetched workflow runs for exact merge commit `a8b581176aac0e6bcf09c049285ed40f4b2b392c`: none returned at audit time;
- fetched combined commit statuses for exact merge commit `a8b581176aac0e6bcf09c049285ed40f4b2b392c`: none returned at audit time;
- compared current startup/next-step text against the merged implementation state;
- rechecked active PR filenames to avoid editing production boundaries owned by #58, #55, or #59.

No local checkout, Windows test execution, .NET build, release build, benchmark, or runtime reproduction was performed by this support agent.

## Not verified

- no exact-main Windows Release Gate was run from this chat;
- no hosted artifact was produced for `a8b581176aac0e6bcf09c049285ed40f4b2b392c`;
- no claim is made that the merge-commit build artifact would be byte-identical to the integration-head artifact;
- no additional archive resource policy (disk free-space reserve or compression ratio) was implemented or verified.

## Recommended next checkpoint

Do not reopen archive streaming implementation.

Instead, when the next full Windows Release Gate runs on canonical main or an eligible exact descendant, verify that the archive source remains unchanged, record the exact commit/run/artifact evidence, and only then advance the repository's last-closed verification provenance.

If a future agent wants to pursue remaining archive resource policy such as destination free-space reserve or compression-ratio policy, scope that as a new independent checkpoint rather than conflating it with the now-integrated streaming cancellation/output-budget work.

## Parallel-work handoff

Preserve updater PR #58, frontend PR #55, and control-plane PR #59 as separate ownership boundaries. Do not merge stale branch-local continuity snapshots over newer canonical state.

The successor must inherit, preserve, and recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them.

**Do not break the chain.**
