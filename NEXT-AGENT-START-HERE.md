# v8.8.71 audit reliability hardening — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active delivery branch: `fix/audit-575-577-browse-selection-v8.8.71-20261002`
Parent canonical main: `350752d315ba6db1d329f726d181bad173b41514`
Issues: #575, #576, #577; partial #559 UX tranche

## Completed predecessor boundary

v8.8.70 PR #572 exact head `c99cf0cd9e3998f2f1ea01f2b6c1612b045b0cb2` passed Workflow Feature `36982837749`, MHW Product Security `36982837913`, and Heaven Toolbox Ownership `36982837948`, then squash-merged as `350752d315ba6db1d329f726d181bad173b41514`.

## v8.8.71 candidate

- Save snapshots copy a live save only through a bounded stability/hash-verification loop and never publish an unstable partial file.
- Windows Release Gate verifies authoritative current main immediately before the first public updater publication mutation; stale queued runs skip publication.
- `CURRENT_REVISION.json` now represents post-integration canonical state and the handoff validator rejects candidate/task-branch state from that canonical bootstrap surface.
- Browse Mods shows an intentional no-selection state, hides mod-specific details until selection, disables install until an exact file is selected, and preserves existing stale-file clearing on mod changes.
- Deterministic regressions cover same-length save mutation, release-workflow guard ordering, continuity-state rejection, and Browse Mods selection gating.

## Verification state

v8.8.71 is not merge-authorized until Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership pass on one exact final head after all metadata is synchronized. No predecessor verification is inherited.

## Unresolved risks

- #558 remains open for broader provider-aware pagination/discovery and scale/performance work.
- #559 remains open for filters, sorting, provider health, loading/stale/partial-failure states, and the rest of the discovery UX overhaul.
- RECOVERY-007 still needs representative installed Windows/runtime discovery proof.
- PR #573 is concurrent provider-update work and must reconcile onto the v8.8.71 canonical tree after this lane integrates.
- External signing/ruleset blockers, artifact-storage constraints, and preserved recovery-branch provenance remain unchanged.

## Ordered continuation

1. Run the required exact-head PR gates on the final v8.8.71 candidate.
2. Repair only concrete failures; do not weaken stable-save, publication-freshness, canonical-continuity, or exact-file selection safety.
3. Refresh `main`, open PR ownership, and mergeability immediately before integration.
4. Merge only one exact green head, then verify remote `main` and closure of #575-#577; keep #559 open for its remaining scope.
5. Reconcile PR #573 and continue #558/#559/RECOVERY-007 without overwriting the v8.8.71 release boundary.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
