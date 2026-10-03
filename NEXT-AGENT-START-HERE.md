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
- Uploaded E2E artifact names bind to the exact tested release SHA rather than the later workflow/evidence commit.

## Verification boundary

Current hosted-Windows closure: v8.8.80 source `6e97751252ce1875550a6cd35630bb63e25a1de3` passed run `37091006888` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.80-heaven-windows-closure.log`.

The tested source remains `6e97751252ce1875550a6cd35630bb63e25a1de3` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and next work

- #559 remains active for broader Browse Mods UX; #558 retains catalog scale/performance work.
- #350/#354 retain external signing/repository-administration prerequisites.
- Representative RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent.
- Actions artifact upload is currently quota-constrained; repository-persisted E2E evidence must remain a reliable fallback.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release-safety rules. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that agent to continue the same recursive handoff. **Do not break the chain.**
