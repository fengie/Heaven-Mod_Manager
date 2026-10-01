# v8.8.64 MHW product — ordered next actions

1. Finish PR #561 first. Required exact-head gates are Workflow Feature PR Gate, MHW Product Security Gate, and Heaven Toolbox Ownership Gate; merge only when all three are green on one final head.
2. If a gate fails, repair only the concrete failure on the current branch, synchronize candidate metadata, and rerun exact-head verification. Do not waive failures.
3. After green verification, refresh canonical `main` and ownership, merge PR #561, verify the merged tree contains the verified source, close #556/#557, and persist integration evidence without another patch bump.
4. Continue #558 with provider-aware pagination/search only where provider capabilities support it. Nexus v3 full-catalog search is currently unsupported and must not be invented.
5. Continue #559 with filters, sorting, provider-health/loading/empty/partial-failure UX after the rich-row tranche is canonical.
6. Preserve issue #281's remaining real provider contracts, external signing/ruleset constraints, RECOVERY-007 Windows discovery proof, and installed WPF acceptance gaps.

## Current verification boundary

v8.8.63 exact head `1090cdde27f979c672d633d9bb513b0c04874e4a` passed all three required gates and merged through PR #555 as `29bcc6fe3c1ac3bb81091f9ba5f02e4e56b9e094`. v8.8.64 candidate source/test commit is `2b49f4f719dfcddfccd3a215a88595df5c779b5e`; later candidate commits synchronize version/docs/continuity. No v8.8.64 integration claim is valid until the final exact PR #561 head passes all required gates.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve and recursively propagate the MHW continuity constitution. **Do not break the chain.**
