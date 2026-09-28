# Open PR / branch disposition audit — 2026-09-28

## Canonical baseline

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Canonical HEAD inspected: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Pre-existing open PRs inspected before opening this support PR: 30
- Self-review PR opened afterward: #36 (this branch), making 31 live open PRs at final validation
- Durable machine-readable ledger: `_AGENT_CONTEXT/BRANCH_DISPOSITION_LEDGER.json`

This support checkpoint is documentation/coordination only. It does not modify production C#, tests, workflows, verification scripts, promoted caches, or historical verification claims.

## Why this task was selected

The repository already had strong narrative integration histories, and company doctrine already required a branch disposition ledger, but there was no current machine-readable project ledger. GitHub still showed many historical support PRs as open and divergent even though their useful work had already been harvested, selectively integrated, superseded, or implemented on canonical main.

That creates a concrete multi-agent integration hazard: a future agent can mistake "open" or "ahead" for "unintegrated" and replay stale branch-local continuity, caches, or production history over newer canonical state.

## Method

1. Confirmed current remote `main` via GitHub compare: `4fd61dd33609a7c55e5aedbaad026266a410f942`.
2. Enumerated all currently open PRs.
3. Compared every open PR head to current `main` to record ahead/behind state.
4. Reconciled each support PR against:
   - `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md`
   - `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-28.md`
   - current `NEXT-AGENT-START-HERE.md`
   - current `NEXT_STEPS.md`
5. Separated live parallel work from historical integrated/redundant work and old unresolved production feature branches.

## Current active parallel ownership

Do not duplicate these lanes:

| PR | Branch | Current scope |
| --- | --- | --- |
| #35 | `agent/archive-streaming-budget-20260928` | archive streaming cancellation + actual-output budgeting |
| #34 | `agent/support-bundle-share-safety-20260928` | support-bundle structured-log sanitization |
| #33 | `agent/support-legacy-migration-hardlink-runtime-20260928` | legacy migration hardlink runtime characterization |
| #32 | `agent/support-auto-updater-release-security-audit-20260928` | auto-updater release/recovery safety audit |

All four were directly ahead of the reviewed main with zero commits behind when inspected.

## Historical support PRs that must not be merged wholesale

PR #36 is recorded separately as the self-review PR for this coordination checkpoint.

The ledger marks PRs #31, #30, #29, #28, #27, #25, #23, #22, #18, #16, #15, #14, #12, #11, #10, #9, #8, #7, #6, #5, #4, #3, and #2 as `historicalIntegrated`.

Their useful work is already represented in canonical main through selective integration, audit harvesting, Learned Rules, later production closure, or a combination of those. Their branch divergence is historical evidence, not an instruction to merge. Use the cited canonical integration record as authority.

PR #24 is `historicalRedundant`: the narrower CAS reparse-policy audit was explicitly skipped because the broader canonical CAS filesystem-identity audit already covered it.

## Old production feature branches that remain unresolved

Two open branches are not support-history branches and were explicitly excluded from support integration:

- PR #13 / `agent/smart-pack-planner-core`: 12 commits ahead and 109 behind current main.
- PR #1 / `codex/complete-mod-workflows`: 10 commits ahead and 185 behind current main.

Neither should be merged from old PR text or old green evidence. A future owner must re-establish current-main intent, inspect the exact diff, decide which behavior is still wanted, and independently verify any salvaged implementation.

## Integration rule going forward

For every integration/support pass:

1. re-check live `main`, open PRs, and branch tips;
2. read `BRANCH_DISPOSITION_LEDGER.json` before selecting or merging support work;
3. treat `historicalIntegrated` and `historicalRedundant` as "reviewed; do not wholesale merge";
4. treat `activeCurrent` as owned parallel work and avoid duplication;
5. treat `unresolvedFeature` as requiring a fresh current-main review;
6. update the ledger in the same checkpoint whenever a branch changes disposition.

GitHub state and newer canonical continuity always outrank the ledger if they conflict.

## Existing strengths

- The 2026-09-27 and 2026-09-28 integration records preserve excellent branch-by-branch reasoning and exact selective-integration decisions.
- Company doctrine already defines required branch disposition states and ledger content.
- Current support agents are using isolated branches and the four newest PRs are cleanly based on the reviewed main.

The missing piece was discoverable current state, not lack of historical evidence.

## Verification performed

- GitHub remote `main` comparison resolved to exact HEAD `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Enumerated the 30 pre-existing open PRs, then recorded PR #36 as the self-review PR after it was opened.
- Compared open PR heads against current `main`; active PRs #32-#35 were ahead / zero behind, while historical lanes were divergent by the recorded counts.
- Re-read the canonical 2026-09-27 and 2026-09-28 integration records and reconciled their explicit dispositions.
- No product test/build result is claimed by this documentation-only checkpoint.

## Things deliberately not changed

- No old PR was closed or branch deleted; historical evidence remains accessible.
- No production source, tests, workflows, verification scripts, release artifacts, or verification caches changed.
- No historical verification claim was promoted or rewritten.
- No new Learned Rule was added because the company trainer already contains the durable rule requiring a branch disposition ledger.

## Successor handoff

Before acting on any old support PR, read the machine-readable ledger and its cited authority. Re-check live GitHub state because the ledger is a reviewed snapshot, not a replacement for remote truth.

Preserve the permanent continuity constitution. The successor must update this ledger when dispositions change and must explicitly require its own successor to inherit, preserve, and recursively propagate the same continuity system to the agent after them.

**Do not break the chain.**
