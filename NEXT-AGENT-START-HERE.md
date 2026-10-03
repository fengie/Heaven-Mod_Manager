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

Current hosted-Windows closure: v8.8.88 source `029b105426ef875bcd302dac3fff9305c395637f` passed run `37144494783` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.88-heaven-windows-closure.log`.

The tested source remains `029b105426ef875bcd302dac3fff9305c395637f` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk

The v8.8.88 candidate changes release publication workflow, provenance policy/tests, and release metadata. Until the exact-final-head PR gates pass and the merged source completes the canonical Windows Release Gate plus installed-client updater E2E, v8.8.88 must not be treated as closed or released. Public `release-index.json` mutation is also unproven until the merged release records its exact immutable source and artifact digests.

## Next action

Require all exact-final-head PR #687 gates to pass. Merge only that exact green head, verify remote `main`, then observe the strict canonical Windows Release Gate and downstream installed-client updater E2E. Confirm the public release repository's `release-index.json` receives the exact immutable release record. Persist closure evidence only for the exact tested source.

## Execution/offload note

This ChatGPT session exposed authenticated GitHub mutation but no callable Heaven Local Bridge / Agent Control execution surface, so implementation used the GitHub path and authoritative CI. No local Windows test is claimed beyond observed workflow evidence.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, updater publication/parity/provenance invariants, exact SDK policy, and durable-evidence privacy. The successor must propagate these continuity obligations to the agent after them.

**Do not break the chain.**
