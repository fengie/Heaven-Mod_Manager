# v8.8.77 updater E2E runner + continuity-state repair — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: updater E2E runner repair plus issue #604 continuity/evidence synchronization

## v8.8.77 behavior

- The updater publication classifier uses the self-hosted Heaven Windows runner instead of the unavailable GitHub-hosted pool while keeping its read-only and fail-closed decision contract.
- Hosted-Windows evidence persistence synchronizes exact source/run proof into `CURRENT_REVISION.json`, `CURRENT_STATE.md`, and this handoff before one evidence commit is pushed.
- The evidence-only commit is not the tested source; exact verification continues to belong to the source SHA recorded inside the closure.
- Current-version closure evidence is validated against machine-readable and Markdown continuity surfaces, with negative fixtures for stale SHA/run and stale candidate instructions.
- v8.8.76 bounded Vortex handoff behavior remains credential-free and isolated from live deployment.
- Steam Workshop remains unsupported for MHW until a reviewed operation-specific contract exists.

## Verification boundary

Current hosted-Windows closure: v8.8.77 source `cd90cff3cec585b6c09e2bb68992247753f6efb9` passed run `37072838484` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.77-heaven-windows-closure.log`.

The tested source remains `cd90cff3cec585b6c09e2bb68992247753f6efb9` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and next work

- Issue #350 remains externally blocked on a real production updater signing identity/public trust anchor and real signed-release E2E.
- Issue #354 remains externally blocked on repository ruleset/branch-protection capabilities and a stable Authenticode publisher identity.
- Issue #281 remains open only for the separate Steam Workshop applicability tranche.
- Issues #558/#559 and representative RECOVERY-005/RECOVERY-007 runtime acceptance remain independent follow-up work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, release supply-chain rules, and the credential-free Vortex boundary. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
