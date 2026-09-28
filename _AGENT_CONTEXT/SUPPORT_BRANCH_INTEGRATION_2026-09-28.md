# Support branch integration — 2026-09-28

## Canonical baseline and scope

- Canonical repository: `fengie/mhw-mods`, branch `main`.
- Pre-integration canonical baseline: `2d6969c7d94438dd64a540dab93fc9a910a8458b`.
- Integration was performed on `heaven2` from a clean main checkout after fetch/prune.
- Recovery branch: `integration-backup-20260928-0327` at the pre-integration baseline.
- The prior 2026-09-27 support inventory remains historical authority for older lanes already harvested there.
- No support branch was merged wholesale. Production/test commits, durable audit documents, and reusable process lessons were selected independently against current main.
- During finalization, canonical main advanced first with hosted evidence commit `a940660` and then with CI race-hardening commit `f825658`. Both were inspected and preserved by rebasing this integration on top; hosted caches remained authoritative and no concurrent canonical work was overwritten.

## Branch dispositions

| Branch | Work | Disposition |
| --- | --- | --- |
| `agent/recursive-source-reparse-hardening-20260927` | Safe recursive traversal for scanner/adoption/Smart Inbox plus adversarial follow-up tests | **Integrated selectively**: production/test commits `f51f729` and `742484b`; stale branch-local caches/handoff snapshots were not copied |
| `agent/recursive-source-reparse-stress-followup-20260928` | Same final recursive candidate | **Skipped duplicate**: same head `8bbb1ba` as the hardening branch |
| `agent/heavy-stress-safety-20260928` | Archive trusted-root containment, fail-before-mutation regressions, process checkpoint rule, evidence | **Integrated selectively**: `cf509b6`, `6ece2af`, `f46ead7`, `333b4ef`, `a1d0119`, `ea5c9f8`, plus durable evidence/LR-010; stale branch caches/handoff snapshots were not copied |
| `agent/support-recursive-source-candidate-audit-20260928` | Independent adversarial review of recursive candidate | **Integrated** as durable audit `RECURSIVE_SOURCE_REPARSE_CANDIDATE_ADVERSARIAL_REVIEW.md` |
| `agent/support-archive-hardening-audit-20260928` | Runtime reproduction of archive trusted-root junction escape | **Integrated** as durable physical-root audit |
| `agent/support-archive-resource-cancellation-audit-20260928` | Runtime reproduction of single-entry cancellation/resource-budget weakness | **Integrated** as future-boundary audit plus generic trainer lesson |

## Older unmerged-by-ancestry refs

The fetch also showed older 2026-09-27 support refs that are not ancestors of current main, including CAS identity/reparse/digest audits, ReplaceFileW characterization lanes, Windows live-containment lanes, diagnostics/network/migration/lifecycle audits, and early test-gap/UI audits. Their worthwhile findings were already harvested by the 2026-09-27 canonical integration and later closure commits. They were rechecked against current continuity and were **not re-integrated** merely because Git ancestry still shows them as unmerged. In particular, `agent/support-recursive-source-containment-audit-20260927` is patch-equivalent to content already present on main.

## Integration order and conflict decisions

1. Added the short-interval durable checkpoint rule first.
2. Integrated recursive-source production/tests, then its adversarial parity/root/cycle follow-up.
3. Ran focused Windows Automation and Integration tests before pushing the first checkpoint.
4. Integrated archive physical-root containment and its fail-before-mutation tests on top.
5. Re-ran focused Windows Automation and Integration tests and pushed a second checkpoint.
6. Integrated documentation-only audits and reusable safety lessons.
7. Preserved current-main architecture when `SmartInboxService.cs` and `HardeningTests.cs` overlapped; Git auto-merged the selected commits cleanly and the combined tests verified the semantics.
8. Did not import support-branch verification caches as canonical evidence. The combined source earned a fresh local Windows verifier/release run instead.

## Combined local Windows verification

Exact integrated source verified before evidence/continuity persistence: `c6c70dd2f8db760ad236b0188cc7026a502afb7a`.

- `scripts/Verify-Release.ps1`: **25/25 PASS**.
- Production function inventory: **615/615 verified**.
- Explicit call sites: **6532 / 0 uncovered**; trace gaps **0**; parse errors **0**.
- Core unit tests: **79/79 PASS**.
- Automation tests: **24/24 PASS**.
- Integration/fault-injection tests: **96/96 PASS**.
- Automation self-test: **11/11 PASS**.
- Strict whole-solution/project analyzers: **PASS**, 0 warnings/errors.
- `scripts/Build-Release.ps1`: **PASS**.
- win-x64 self-contained ReadyToRun publish: **PASS**.
- Local release ZIP SHA-256: `359B12050437AEF0EE9695FEFCE4B8424475413EF54299B2DAC345F5C4E32B21`.

## Residuals and next boundary

The integrated reparse protections remain path-based. The documented check/open TOCTOU window and hardlink policy are not claimed solved. A dedicated file-symlink/reparse-leaf runtime fixture remains unexecuted because the local Windows account lacked symlink-creation privilege; directory-junction root/descendant/cycle cases are covered.

Archive extraction physical-root containment is implemented. The independent archive resource/cancellation audit remains **unimplemented**: a runtime probe showed cancellation during a single large entry can be ignored by the synchronous payload write and still return success. Keep streaming cancellation/actual-output budgeting as a separate production checkpoint.

Hosted Windows Release Gate `36392282315` passed exact production integration commit `d66bff290f197236ec43c9b37d2b015ab2ee5fe8`; GitHub Actions persisted the hosted verification cache/evidence at `a94066004660e4d542f5d4a528c7e5e22bdea9cb`. Hosted release ZIP SHA-256: `A264C5108DDEA0E3301BFF0E33D7A3E7DA3A92A566739C865AE28B8111BFAFAB`. The separate local Windows closure remains useful corroborating evidence for the same production code plus documentation.

## Continuity decision

LR-010 was added: a containment check after mutation is not fail-closed. The generic company safety doctrine also records the generalized pre-mutation validation lesson and the independent stream-level cancellation/resource-budget lesson.

The successor must inspect current `origin/main` before acting, preserve exact verification provenance, and inherit/preserve/recursively propagate the permanent continuity constitution and active Learned Rules to the agent after them. **Do not break the chain.**
