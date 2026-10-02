# v8.8.71 catalog installed-origin snapshot dedup — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `agent/issue-569-provider-update-snapshot-20261002`
Issue: #569
PR: #573
Parent canonical main: `350752d315ba6db1d329f726d181bad173b41514`

## Completed predecessor boundary

v8.8.70 PR #572 exact head `c99cf0cd9e3998f2f1ea01f2b6c1612b045b0cb2` passed Workflow Feature `36982837749`, MHW Product Security `36982837913`, and Heaven Toolbox Ownership `36982837948`, then squash-merged to main as `350752d315ba6db1d329f726d181bad173b41514`.

The primary stale-profile rediscovery path is therefore canonical, but review found two post-merge semantic gaps that are now tracked explicitly in issue #578: canonical MHW adapter recovery from a stale generic profile, and whole-set handling when multiple profiles share one game root.

## v8.8.71 candidate

- Add the optional `IInstalledCatalogOriginSnapshotProvider` contract so providers can return authoritative mod metadata plus exact file identity from one hydration.
- Keep the existing `IModCatalogProvider` two-call fallback for providers that do not implement the snapshot contract.
- Implement the snapshot path for GameBanana, whose file-list path otherwise delegates through `GetModAsync` and duplicates the same detail request during installed-origin update checks.
- Preserve capability gating, cancellation, provider health/error classification, exact mod/file identity validation, and fail-closed replacement selection.
- Add deterministic request-count coverage proving one GameBanana installed-origin update check performs exactly one `/Core/Item/Data` request.
- Keep Nexus behavior unchanged: it does not currently advertise `CatalogProviderCapabilities.Updates`, so no unsupported update path is enabled.

## Verification state

The reconciled source/test head `51aeff9d0c7da2843949b0f307ac1f4907982aef` passed Workflow Feature run `36982514282` on the exact checked-out SHA while based on v8.8.69. That evidence is useful but superseded for merge authorization after reconciliation onto v8.8.70 and the v8.8.71 metadata commit.

Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final v8.8.71 PR #573 head before merge.

## Unresolved risks

- Issue #578 tracks two v8.8.70 stale-profile lifecycle gaps and must not be treated as closed by #571's merge.
- RECOVERY-007 still needs representative installed Windows/runtime discovery proof.
- #558 remains open for broader provider-aware catalog pagination/discovery and scale/performance work.
- #559 remains open for Browse Mods filtering, sorting, provider health, and richer discovery states.
- External signing/ruleset blockers, artifact-storage constraints, and preserved recovery-branch provenance remain unchanged.
- Local Codex on `heaven` remains usage-limit blocked until 2026-10-07; exact GitHub/Heaven CI is the verification authority for this candidate.

## Ordered continuation

1. Run Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on one exact final PR #573 v8.8.71 head.
2. Repair only concrete failures; preserve the v8.8.70 game-profile behavior plus #569's exact-identity/fail-closed catalog invariants.
3. Refresh `main`, issue #569 ownership, and PR mergeability immediately before integration.
4. Squash-merge only the exact green head; verify canonical main contains v8.8.71 and issue #569 closes.
5. Keep issue #578 live for the known v8.8.70 semantic follow-ups; do not silently fold those fixes into this catalog patch.
6. Continue non-overlapping #558/#559/RECOVERY-007 work after the v8.8.71 boundary is canonical.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
