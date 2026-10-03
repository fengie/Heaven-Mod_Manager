# v8.8.79 automation state integrity — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issues #619 and #620

## v8.8.79 behavior

- Save snapshot pruning never recursively follows a reparse-point snapshot directory. It removes only the immediate link and deletes the index row only after unlink succeeds; unlink failure keeps the row retryable.
- Last-known-good change detection compares the union of saved and current mod IDs, reporting removals as well as additions, enable-state changes, and priority changes.
- Mod identity comparison remains case-insensitive and output ordering deterministic.

## Verification boundary

v8.8.78 source `e6e6a91b40ac281f38d0f11dc6b7a77052ca4071` is the last closed hosted-Windows source, run `37082825628`. v8.8.79 changes Automation source/tests and release metadata, so it requires fresh exact-input verification.

## Unresolved risks and next work

- #559 remains active for broader Browse Mods UX; #558 retains catalog scale/performance work.
- #350/#354 retain external signing/repository-administration prerequisites.
- Representative RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, filesystem containment, and release-safety rules. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that agent to continue the same recursive handoff. **Do not break the chain.**
