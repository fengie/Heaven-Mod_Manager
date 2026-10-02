# v8.8.76 Vortex handoff interoperability — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #281 / PR #601 bounded Vortex interoperability

## v8.8.76 behavior

- Vortex remains an optional interoperability boundary, not a catalog provider or private-state integration.
- The app imports/exports only the reviewed v1 handoff schema for Monster Hunter: World.
- Handoffs cannot carry generic source URLs, cookies, API keys, bearer tokens, Vortex databases, deployment folders, or private Redux/state.
- Wrong-game, malformed, unknown-field, unsafe-path, ambiguous, missing, and hash-mismatched entries fail closed.
- Import creates an isolated manager profile and never mutates live game files until the normal explicit profile-apply flow.
- Export uses same-directory temporary files plus atomic replacement so an interrupted write cannot corrupt an existing handoff.
- Steam Workshop remains unsupported for MHW until a reviewed operation-specific contract exists.

## Verification boundary

The last closed predecessor is v8.8.75 source `5d864d4d5189e5ad7c0ec535886c506fcc07c513` with hosted Windows verification evidence run `37015488962` persisted on canonical main. v8.8.76 requires fresh exact-head PR/security/release gates on the final #601 candidate before integration; predecessor evidence is not authorization for changed interoperability, tests, or version metadata.

## Unresolved risks and next work

- Issue #350 remains externally blocked on a real production updater signing identity/public trust anchor and real signed-release E2E.
- Issue #354 remains externally blocked on repository ruleset/branch-protection capabilities and a stable Authenticode publisher identity.
- Issue #281 remains open only for the separate Steam Workshop applicability tranche.
- Issues #558/#559 and representative RECOVERY-005/RECOVERY-007 runtime acceptance remain independent follow-up work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, release supply-chain rules, and the credential-free Vortex boundary. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
