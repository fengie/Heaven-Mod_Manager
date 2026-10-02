# v8.8.72 audit reliability hardening — current handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Delivery branch: `fix/audit-findings-575-577-20261002`
Issues: #575, #576, #577; partial #559 selection-state tranche
Parent canonical main: `8dc0294e2415c02a089c845d0d5bbc715c467931`

## Completed predecessor boundary

v8.8.71 PR #573 exact head `90fe998b2024aa43f7e3999185c87a98cac6a016` passed Workflow Feature `36983859719`, MHW Product Security `36983859681`, and Heaven Toolbox Ownership `36983859667`, then squash-merged as `cd189422d1def33d89f2ebaffb30091f2db4ad23`. Current main also includes #580's reduced release-evidence artifact footprint.

## v8.8.72 candidate

- Save snapshots publish only a stable, hash-verified live-save copy; unstable/cancelled capture cleans incomplete payloads and never records success.
- Same-length/same-timestamp mutation is covered so metadata-only checks cannot falsely certify a torn save.
- Windows Release Gate re-reads canonical main immediately before the first updater publication mutation and all publication/parity steps require the positive freshness output.
- `CURRENT_REVISION.json` is post-integration canonical state; candidate/task-branch/active-PR state is rejected mechanically.
- Browse Mods has an intentional no-selection state; mod controls remain hidden until a mod is selected and install is disabled until an exact file is selected.

## Verification state

v8.8.72 is not merge-authorized until Workflow Feature, MHW Product Security, Heaven Toolbox Ownership, and the updater publication PR gate pass on the exact final reconciled head. No predecessor verification is inherited.

## Unresolved risks

- Issue #578 still tracks two stale-profile lifecycle edge cases after v8.8.70.
- #558 remains open for broader provider-aware pagination/discovery and deterministic scale/performance work.
- #559 remains open for filters, sorting, provider health, loading/stale/partial-failure states, and the rest of the discovery UX overhaul.
- RECOVERY-005 and RECOVERY-007 still require representative installed Windows/runtime acceptance.
- External signing/ruleset blockers and preserved recovery-branch provenance remain unchanged.

## Ordered continuation

1. Run all required exact-head gates on the final v8.8.72 PR head.
2. Repair concrete failures only; do not weaken the stable-save, publication-freshness, canonical-continuity, or exact-file selection invariants.
3. Refresh canonical main and PR mergeability immediately before integration.
4. Merge only the exact green reconciled head; verify remote main and automatic closure of #575-#577. Keep #559 open for its remaining scope.
5. Continue #578, #558/#559, RECOVERY-005, and RECOVERY-007 from fresh main.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
