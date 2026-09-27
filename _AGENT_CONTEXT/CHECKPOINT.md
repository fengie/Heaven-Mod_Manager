# Current checkpoint — workflow expansion

Updated 2026-09-27. **Automated Linux and Windows validation passed. Native WPF/game acceptance and release packaging remain pending.**

## User instruction

Push at meaningful checkpoints and save the plan, completed work, evidence, limits and next steps before credits expire. Transfer relevant knowledge to Git and explicitly require future agents to do the same. This policy is in AGENTS.md and CONTINUITY_PROTOCOL.md.

## Where to continue

- Repository: `fengie/mhw-mods`; canonical base `main`.
- Active branch: `codex/complete-mod-workflows`.
- Draft PR: https://github.com/fengie/mhw-mods/pull/1 — not merged.
- Prior evidence checkpoint: `4555d97e1e79764dd70c59b6ada5c5dd071b948e`.
- This documentation checkpoint adds `SESSION_HANDOFF_2026-09-27.md`; read it for complete task scope, architecture, limitations, commands, publishing mechanics and a native acceptance matrix.
- Exact current remote SHA is in Git history. Local checkout has synthetic history; use actual remote parents for GitHub API commits.

## Plan

1. DONE — inspect repository/training and preserve existing invariants/branding.
2. DONE — implement the twelve workflow areas and integrated UI; see WORKFLOW_IMPLEMENTATION.md and docs/WORKFLOWS.md for boundaries.
3. DONE — strict Linux build, 179 tests, 11 self-tests, trace scan and handoff.
4. DONE — push PR and verify initial remote file parity.
5. DONE — fix Windows raw-string fingerprint drift with `*.cs text eol=lf`; native CI passes.
6. DONE — persist user checkpoint policy, exact evidence and complete session knowledge.
7. NEXT — hands-on WPF interaction and real-game smoke tests in a disposable workspace; normal release verification/packaging. Do not merge without authorization.

## Evidence

- Strict Linux/Windows builds: 0 warnings/errors.
- Core 79/79; Automation 39/39; Integration 61/61. Total 179, including 21 new workflow tests.
- Self-test 11/11.
- Scan: 683 functions; 562 known-good; 121 awaiting normal promotion; zero trace/parse/call-site gaps. No manual promotions.
- Windows PASS: https://github.com/fengie/mhw-mods/actions/runs/36318654840 (a11ca39).
- Latest CI configuration PASS: https://github.com/fengie/mhw-mods/actions/runs/36318708338 (66a48bf).
- Logs and exact evidence commits: CURRENT_REVISION.json, VERIFICATION.md, EVIDENCE/workflow-*.
- CI skips documentation-only checkpoints; run Test-AgentHandoff locally for these.

## Important boundaries

No new packaged release was produced. The version remains 8.8.0 with unreleased workflow changes. FOMOD external dependencies fail closed and cross-version options require review. Recipes do not download payloads; family restore is explicit; profiles share global rules. Graph is a scoped persisted-relationship view. Adapter SDK is an initial contract, not completed semantic support for every game. See the full handoff before making broader claims.

**Next agent: update these facts, push each meaningful checkpoint, and require your successor to do the same. Do not break the chain.**

## Research continuation — 2026-09-27

Batch 1 complete: RESEARCH_FOMOD_2026-09-27.md records Inbox partial-import/result risks, FOMOD compatibility differences, and concrete fixtures. These are source observations and proposed tests, not fixed regressions. Next: transaction/recovery and diagnosis audit, then Windows UI acceptance research. Save and push each batch.
