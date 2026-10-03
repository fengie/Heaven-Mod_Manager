# v8.8.80 updater E2E evidence persistence — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #622

## v8.8.80 behavior

- Updater installed-client E2E persistence restores complete canonical-main history before deciding whether the tested release source is an ancestor of current main.
- The persistence step explicitly refreshes `refs/remotes/origin/main` before ancestry and path-drift classification.
- Divergent/release-relevant drift remains fail-closed; evidence/cache-only advancement remains eligible for durable E2E closure persistence.
- Regression coverage enforces unshallow-before-`merge-base` ordering.

## Verification boundary

v8.8.79 source `6a10008fdcaae5112653593d62b41f5ef4668b36` passed hosted-Windows run `37085548752` and installed-client E2E run `37085941626`, but the E2E repository-persistence fallback skipped against evidence-only main `dc114b07935ca44ba19af82ae5c77a3ead09f4db` because the checkout was shallow. v8.8.80 changes release workflow/test inputs and version metadata, so it requires fresh exact-input Windows verification and a fresh installed-client E2E with durable evidence persistence.

## Unresolved risks and next work

- #559 remains active for broader Browse Mods UX; #558 retains catalog scale/performance work.
- #350/#354 retain external signing/repository-administration prerequisites.
- Representative RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent.
- Actions artifact upload is currently quota-constrained; repository-persisted E2E evidence must remain a reliable fallback.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release-safety rules. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that agent to continue the same recursive handoff. **Do not break the chain.**
