# Live routing reconciliation — 2026-09-28

## Canonical baseline

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Exact canonical HEAD inspected before this branch: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`
- That commit integrates PR #61, the archive-streaming verification-provenance repair.
- This support checkpoint changes continuity/routing documents only. It does not edit product code, tests, workflows, verification caches, updater scripts, frontend code, or Agent Control implementation.

## Why this task still exists after PR #61

PR #61 correctly repaired the archive-streaming provenance/status documents: archive cancellation/output budgeting is implemented, its local integration-head evidence is distinguished from hosted exact-main evidence, and old “archive is next” text is no longer authoritative.

However, the hot coordination layer still routes successors through stale work:

- `_AGENT_CONTEXT/CURRENT_REVISION.json` still identifies `agent/auto-updater-publication-fix-20260928` / C12 as the working/current routing state.
- `_AGENT_CONTEXT/handoff-manifest.json` still makes the old C12 continuation the primary next-agent obligation.
- `NEXT-AGENT-START-HERE.md` still opens with the old isolated C12 repair branch even though the live updater lane has advanced to PR #58.
- `_AGENT_CONTEXT/CURRENT_STATE.md` still opens with the old C12 publication repair.
- `_AGENT_CONTEXT/NEXT_STEPS.md` has accurate archive status after PR #61 but still begins with the older C12 integration sequence rather than the live parallel ownership map.

In a multi-agent repository, this remaining drift is operationally significant: it can send a new agent onto a superseded branch, duplicate an owned lane, or replay stale shared continuity over newer main.

## Live parallel ownership snapshot

At the final pre-edit GitHub check:

| PR | Lane | Head | Ownership |
| --- | --- | --- | --- |
| #55 | Frontend UX / responsive workflows | `05672c6fb5bcb4f8a71efefc22985d11d75ae3d8` | active production lane |
| #58 | Updater post-upload stale-main publication window | `ce2c1fef491a06f26f76d4cba0201af76c68196c` | active updater production lane |
| #59 | Agent Control v2 engineering control plane | `41d377d1c6ce5b0b0f747aa37484eaadc9033ce5` | active control-plane production lane |
| #62 | Persisted game-profile ID path-containment audit | `3fe92bd111dc4a02c8d3dc9b0120ecf6761f041a` | active support lane |
| #64 | Launch observation persistence atomicity audit | `90be320306122e39c5669449305452a373324eaa` | active support lane; specializes SQLite D2 |
| #65 | Updater publication verification race closure | `98115515b2dc5fb256560c50ed8c40d08abb21f6` | active updater integration lane; incorporates #58 and says it supersedes #58 once merged |
| #66 | Agent Control v2 safety invariant audit | `0d48692749e3b25bf32c179e114323e4f45d05bd` | active support review of #59; no competing implementation |
| #68 | Archive streaming failure-cleanup semantics audit | `e24444c0c9867dda0dcf8b22db3f613878ae7478` | active support lane; separate from integrated #61 provenance |

Stored PR heads are a checkpoint, not durable authority. The active set changed repeatedly during this support task: #64/#65/#66/#68 appeared during finalization, and #58 advanced. Successors must re-query live GitHub before selecting work. In particular, do not duplicate #65's updater integration, #66's review of #59, #64's launch-observation audit, or #68's archive-cleanup audit.

## Verification truth preserved

This repair does not rewrite historical verification evidence.

- The repository still records the prior last-closed hosted exact-source verification at `d66bff290f197236ec43c9b37d2b015ab2ee5fe8` / Windows Release Gate `36392282315`.
- PR #57 local integration-head evidence remains exactly as repaired by PR #61: integration head `fd8b48fc92e6f5e64591fd1938b7ccce5ac94083`, verifier 25/25, 728/728 functions, 7,772 call sites / 0 uncovered, Core 79/79, Automation 24/24, Integration 177/177, self-test 11/11, strict build/analyzers and Build-Release PASS, artifact SHA-256 `AC3571853650CFA91243199B23A44007488F9244780FCBD18A7A38552B652734`.
- This checkpoint makes no hosted-gate claim for current main `3d241558...`.

## Repair scope

This branch:
- adds this routing audit;
- updates `CURRENT_REVISION.json` with live canonical HEAD and the current owned-lane map;
- updates `handoff-manifest.json` so successors discover this audit and re-query live GitHub;
- prepends current routing overlays to `NEXT-AGENT-START-HERE.md`, `CURRENT_STATE.md`, and `NEXT_STEPS.md`;
- adds this audit to `README_FIRST.md` discovery.

Historical sections remain intact for provenance.

## Learned-rule / trainer decision

No new Learned Rule or company-trainer rule is added. Existing doctrine already says current canonical repository state outranks stale prompts/handoffs and multi-agent ownership must be re-checked before acting. This task repairs enforcement/discovery of that existing rule rather than discovering a new invariant.

## Verification actually performed

- canonical GitHub HEAD and recent merge history inspected;
- open PRs and exact heads inspected repeatedly;
- PR #61 archive-provenance integration inspected;
- current hot continuity files inspected from exact canonical main;
- JSON files parsed before write;
- all changes isolated on a branch based exactly on `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`.

Not performed in this chat environment:
- `scripts/Test-AgentHandoff.ps1`;
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1`;
- .NET compile/tests;
- Windows Release Gate;
- verification-cache promotion.

Because this is continuity/documentation-only work, it claims no product-runtime verification. A merger with an authorized checkout should run the two handoff checks before integration if required by repository policy.

## Successor handoff

Start from live canonical `main`, then re-query open PRs. Do not resume the old C12 branch merely because historical top sections still describe it. At this checkpoint updater work is split between #58 and the newer #65 integration (which states it supersedes #58 once merged); #55 owns frontend UX; #59 owns Agent Control v2 while #66 owns its safety review; #62 owns game-profile ID containment; #64 owns launch-observation atomicity; and #68 owns archive streaming failure-cleanup review.

Preserve exact verification provenance and the permanent continuity constitution. Require your successor to inherit, preserve, and recursively propagate the same constitution to the agent after them.

**Do not break the chain.**
