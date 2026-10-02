# v8.8.65 MHW product — ordered next actions

1. Obtain installed Windows/WPF acceptance for issue #556: on a build containing v8.8.65, confirm the closed header selector shows the human-readable game name and switching games preserves readable selected/dropdown text.
2. Record that runtime evidence and close #556 only if the installed-client check passes.
3. Preserve the verified source boundary from PR #562 head `236d3604f1df245f9c224eda3f21f2177979cfd2` merged as `c3f238cbe4f1850763736bac999af0577a4606c5`.
4. Continue #558 with provider-aware pagination/search only where provider capabilities actually support it.
5. Continue #559 with provider/category filters, sorting, provider health, loading/empty/stale/partial-failure states, and clearer result-coverage UX.
6. Preserve issue #281's real provider contracts, external signing/ruleset constraints, RECOVERY-007 Windows discovery proof, and other documented evidence gaps.

## Closed verification boundary

v8.8.65 exact PR #562 head `236d3604f1df245f9c224eda3f21f2177979cfd2` passed Workflow Feature PR Gate run `36963458484`, MHW Product Security Gate run `36963458384`, and Heaven Toolbox Ownership Gate run `36963458431`, then merged to canonical main as `c3f238cbe4f1850763736bac999af0577a4606c5`.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve and recursively propagate the MHW continuity constitution. **Do not break the chain.**
