# v8.8.88 public release provenance — candidate handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #686 / PR #687

## v8.8.88 behavior

- Generate deterministic public updater provenance from exact verified release bytes and source identity.
- Append/reconcile the public `release-index.json` only after immutable public/private parity.
- Use idempotent exact retries, fail-closed identity-conflict handling, and bounded compare-and-swap retry for concurrent writers.
- Re-download immutable published assets when local rerun bytes do not match release metadata before generating provenance.
- Preserve semantic nonpublication: no exact immutable release means no provenance mutation.
- Support one or many artifact paths safely under Windows PowerShell 5.1 strict mode.
- Preserve canonical exact .NET SDK provisioning and roll-forward policy.

## Verification boundary

Last closed canonical Windows source is v8.8.87 `166369c60d6b5a726fbccd1eecac073ebdaa7ce3` / run `37140984099`.

The pre-version provenance head `c83e29054550d696701b34ba74bf66d5902dbbfe` passed Updater Publication, Product Security, and Workflow Feature gates after repairing Windows PowerShell scalar path handling. The reconciled v8.8.88 final head includes the verified v8.8.87 evidence-only successor `8bb6fda2690f77957d15c5c6585ed5081f333e77` and changes release inputs, so earlier greens are supporting evidence only and must not authorize merge.

## Unresolved risk

The v8.8.88 candidate changes release publication workflow, provenance policy/tests, and release metadata. Until the exact-final-head PR gates pass and the merged source completes the canonical Windows Release Gate plus installed-client updater E2E, v8.8.88 must not be treated as closed or released. Public `release-index.json` mutation is also unproven until the merged release records its exact immutable source and artifact digests.

## Next action

Require all exact-final-head PR #687 gates to pass. Merge only that exact green head, verify remote `main`, then observe the strict canonical Windows Release Gate and downstream installed-client updater E2E. Confirm the public release repository's `release-index.json` receives the exact immutable release record. Persist closure evidence only for the exact tested source.

## Execution/offload note

This ChatGPT session exposed authenticated GitHub mutation but no callable Heaven Local Bridge / Agent Control execution surface, so implementation used the GitHub path and authoritative CI. No local Windows test is claimed beyond observed workflow evidence.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, updater publication/parity/provenance invariants, exact SDK policy, and durable-evidence privacy. The successor must propagate these continuity obligations to the agent after them.

**Do not break the chain.**
