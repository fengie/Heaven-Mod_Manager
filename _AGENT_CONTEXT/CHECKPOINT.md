# Checkpoint — workflow expansion

Updated: 2026-09-27. User explicitly requested frequent checkpoint pushes and handoff notes before credits expire.

## Durable remote state

- Repository: `fengie/mhw-mods`; canonical base is `main`.
- Active branch: `codex/complete-mod-workflows`.
- Draft PR: https://github.com/fengie/mhw-mods/pull/1
- Code/evidence commit: `02fa1dd5c3e98d319067043598bfdfd32617e65f`.
- Exact revision metadata commit: `c4c6db8521358e1af778b19456411d638b62c0dd`.
- All 221 remote blob hashes matched the tested local files after publication.

## Completed

Implemented the twelve requested workflow areas, integrated UI, regression tests,
Windows CI, and documentation. See `WORKFLOW_IMPLEMENTATION.md`, `CURRENT_REVISION.json`,
and `docs/WORKFLOWS.md` for details and deliberate feature boundaries.

Linux strict solution build passed with zero warnings/errors; Core 79, Automation 39,
Integration 61 all pass (179 tests); self-test 11/11; function scan has 683 functions,
562 known-good and 121 pending normal verification, with zero trace/parse/call-site gaps.
Handoff preflight and whitespace checks pass. No manual fingerprint promotions.

## Current plan and next action

1. DONE — inspect canonical repo/training and preserve branding/transaction invariants.
2. DONE — implement and integrate the requested workflow areas.
3. DONE — Linux validation, fault-injection tests, evidence and continuity notes.
4. DONE — publish draft PR and verify remote file parity.
5. IN PROGRESS — Windows build, all 179 tests, and 11 self-tests passed. Fix the checkout line-ending trace-scan failure, push, and observe the rerun.
6. PENDING — record final Windows CI outcome in CURRENT_REVISION/VERIFICATION. Native
   WPF interaction and real-game smoke tests remain a separate Windows acceptance step.

Windows run: https://github.com/fengie/mhw-mods/actions/runs/36318260759
Job `108616978328`: strict build, backend suites, self-test and handoff all passed.
The final function scan failed on an unchanged legacy raw SQL string: Windows
autocrlf changed literal token bytes relative to the LF trusted snapshot.
`.gitattributes` now pins `*.cs` to LF, preserving exact fingerprints without weakening
the verifier or manually promoting booleans. Await the new Windows run. A second push-triggered run also exists (`36318246547`). Query the
latest run rather than assuming these statuses remain current.

## Continuation mechanics

The local checkout was materialized from GitHub and has a synthetic baseline history;
its local commit hashes differ from GitHub. Remote Git data API parent SHAs are the
source of truth. Normal direct git clone/push lacked credentials in this environment;
connected GitHub tools successfully create trees, commits, refs and PRs. Never overwrite
new main changes or force-push. This branch is not merged.

Runtime used: `/workspace/scratch/bcf29506d2df/toolchain/dotnet/dotnet` (.NET 10.0.401),
PowerShell at `/workspace/scratch/bcf29506d2df/toolchain/pwsh/pwsh`. Local repo:
`/workspace/scratch/bcf29506d2df/mhw-mods`. These paths are transient; remote commits are durable.
Build with `-m:1 -p:EnableWindowsTargeting=true -warnaserror` on Linux. Run compiled test
DLLs in process (`-noLogo -noColor -maxThreads 2`); do not use the sandbox-blocked named-pipe
VSTest path. The integration log deliberately prints failure diagnostics for negative
verifier fixtures; inspect the final suite summary (zero failures).

**Do not break the chain. Push and update this checkpoint at each meaningful milestone.**

Checkpoint policy commit: `172d22118f2b33feb03b56b6971358195f628aa3`.
