# v8.8.64 MHW product — ordered next actions

1. Continue #558 with provider-aware pagination/search only where provider capabilities actually support it. Nexus v3 full-catalog search is currently unsupported and must not be invented.
2. Continue #559 with provider/category filters, sorting, provider health, loading/empty/stale/partial-failure states, and clearer result-coverage UX.
3. Preserve the integrated v8.8.64 invariants: human-readable game selector, safe HTTPS catalog thumbnails, rich metadata, exact-file acquisition identity, recycling virtualization, 100-item/provider first tranche, and 1000 visible cached results.
4. Obtain installed Windows/WPF visual acceptance for the repaired selector and live remote-thumbnail behavior when an authorized runtime path is available.
5. Preserve issue #281's remaining real provider contracts, external signing/ruleset constraints, RECOVERY-007 Windows discovery proof, and other documented evidence gaps.
6. Retire temporary v8.8.64 work branches only after confirming no unique unintegrated content remains.

## Closed verification boundary

v8.8.64 exact PR #561 head `3ced8b41041909d91b06902631ef36085bfd7489` passed Workflow Feature PR Gate 36895834301, MHW Product Security Gate 36895834289, and Heaven Toolbox Ownership Gate 36895834269, then merged to canonical main as `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`. Issues #556 and #557 are closed; #558 and #559 remain intentionally open.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve and recursively propagate the MHW continuity constitution. **Do not break the chain.**
