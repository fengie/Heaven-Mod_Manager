# v8.8.85 feature verification decoupling — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #675 / PR #678

## v8.8.85 behavior

- Feature PRs use `Verify-Release.ps1 -FeatureCandidate` so exact-head product verification does not require the branch to own the next global patch metadata first.
- Candidate mode defers only release-version/current-version surface parity; it does not skip repository identity, canonical-state shape, security policy, function verification, strict builds/analyzers, tests, integration/fault injection, self-test, or continuity/toolbox ownership.
- Canonical `windows-release-gate.yml` remains on the default full verifier. CI policy rejects any `-FeatureCandidate` use in that release/publication workflow.
- Regression coverage mutates VERSION in an isolated governance fixture and proves canonical mode rejects the drift while feature-candidate mode accepts it.

## Verification boundary

Current hosted-Windows closure: v8.8.85 source `d50b5defe23209119b417b56e7047e14e4c37032` passed run `37127511216` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.85-heaven-windows-closure.log`.

The tested source remains `d50b5defe23209119b417b56e7047e14e4c37032` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Execution/offload note

This ChatGPT runtime did not expose callable Heaven Local Bridge or Agent Control operations. Ownership was therefore acquired through the authorized Toolbox GitHub CAS claim fallback, and implementation used the authenticated GitHub path. No HMAC boundary was weakened, no heaven-to-heaven2 proxy was used, and no local-agent execution was fabricated.

## Unresolved risk

The final metadata-synchronized PR #678 head still requires fresh exact-head verification before merge. Do not reuse run `37125208523` after the metadata commits.

## Next action

Observe the fresh exact-final-head PR #678 gates. Merge only that exact green head, verify remote `main`, then verify the canonical Windows release path still runs the full strict verifier and persist exact closure evidence normally.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, updater topology/storage-retention invariants, filesystem containment, durable-evidence privacy, and the candidate-vs-canonical verification separation. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
