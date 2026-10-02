# v8.8.69 provider-aware Browse Mods search — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `agent/issue-558-provider-search-v8.8.69-r2-20261002`
Issue: #558
Canonical main at reconciliation start: `601496093430214791395ba1bfcfadd3b0262ad2`

## Closed predecessor

Issue #556 is complete. v8.8.68 PR #567 exact head `966fe005b279c901b6782f4f3d8c466ba503908a` passed Workflow Feature `36978477603`, MHW Product Security `36978477581`, and Heaven Toolbox Ownership `36978477580`, then merged as `7def58c1b16d115e1555738ebad51717d1c1f752`.

Hosted Windows verification `36978710736` passed 26/26 for source `7def58c1b16d115e1555738ebad51717d1c1f752` and produced updater build 382. Updater Installed Client E2E `36979261045` then passed the real packaged update path with `selectorDisplayText = "Updater E2E Fake Game"`, Switch enabled, Settings enabled, and rollback PASS. Durable evidence is on canonical main at `601496093430214791395ba1bfcfadd3b0262ad2`.

## v8.8.69 candidate

This reconciles the useful #558 work from stale/conflicting PR #565 onto current v8.8.68 main without importing its old version/continuity snapshots.

- Keep per-keystroke Browse Mods filtering local to SQLite/FTS.
- Make explicit Search contact only configured providers advertising `CatalogProviderCapabilities.Search`.
- Persist provider-returned rows through `CatalogSyncService` and the source-aware cache before display.
- Preserve per-provider failure isolation and matching cached results.
- Do not probe Nexus Mods or GameBanana for unsupported full-catalog text search.
- Clarify cached filtering versus explicit provider search in the UI.
- Add focused regression coverage proving capability gating and cache-only debounce behavior.

This tranche does **not** complete #558. Broader provider-aware pagination/browse expansion and deterministic scale/performance coverage remain open.

## Verification state

No v8.8.69 green claim exists yet. The semantic predecessor in PR #565 passed all three required exact-head gates on `a03e2a73e806c3b49dacf382c2061255fc79521a`, but those greens do not authorize this reconciled candidate because main advanced through v8.8.68.

The live ChatGPT route did not expose Agent Control / Heaven Bridge dispatch tools during this reconciliation, so no local-agent build/test result is claimed. The existing HMAC boundary was not weakened.

## Unresolved risks

- **Unresolved risk:** v8.8.69 is not merge-authorized until Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership all pass on one exact final head.
- **Unresolved risk:** issue #558 remains open after this tranche for provider-aware pagination/browse expansion and deterministic scale/performance coverage; the repository search path currently clamps results below the UI's requested 1000-row capacity and needs a separate follow-up fix.

## Ordered continuation

1. Open a PR for this v8.8.69 branch against fresh `main`.
2. Require Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership on the exact final head.
3. Repair only concrete failures; preserve #556's selector peer-value behavior.
4. Refresh canonical main and issue/PR ownership immediately before integration.
5. Merge only the exact green head and verify the intended tree on remote main.
6. Close stale PR #565 as superseded only after its unique provider-search semantics are integrated.
7. Keep #558 open for pagination/browse/scale work, then continue #559 as a separate UX tranche.

## Successor obligation

The successor must bootstrap from current `fengie/heaven-toolbox@main` before current MHW `main`, read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` in full, preserve this exact verification/integration boundary, and recursively propagate the same obligation to the next agent. **Do not break the chain.**
