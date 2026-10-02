# v8.8.65 MHW product — ordered next actions

1. Finish reopened issue #556 first: verify the v8.8.65 selected-content-template candidate with Workflow Feature PR Gate, MHW Product Security Gate, and Heaven Toolbox Ownership Gate on one exact final head.
2. If a gate fails, repair only the concrete failure, refresh main/ownership, and rerun exact-head verification; do not waive failures.
3. Merge only after all required gates are green and fresh-main reconciliation preserves concurrent work; verify remote main contains the `ItemTemplate`-backed selected presenter.
4. Obtain installed Windows/WPF visual confirmation that the closed header selector shows `GameProfile.DisplayName` (for example, `Monster Hunter: World`) rather than raw record/debug text.
5. Close #556 only after integration and recorded acceptance evidence.
6. Then continue #558/#559 without reopening unrelated catalog/provider scope.

## Closed verification boundary

v8.8.64 exact PR #561 head `3ced8b41041909d91b06902631ef36085bfd7489` passed all required gates and merged as `a39064643f97aa5a0df80bf7c917d260b3d7e7a1`. v8.8.65 is a new candidate and inherits no green claim across changed source.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve and recursively propagate the MHW continuity constitution. **Do not break the chain.**
