# v8.8.81 allowlisted updater E2E evidence — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #632

## v8.8.81 behavior

- Persist only the versioned allowlisted installed-client updater E2E projection; keep raw runtime diagnostics transient.
- Bind durable target-source identity to the exact workflow-tested source SHA.
- Validate old release identity, successful update confirmation, rollback restoration, UI acceptance, and fixed sentinel hashes before persistence.
- Exclude runner-local paths/usernames, process/attempt IDs, target-only path inventories, logs, timestamps, runner environment metadata, and unknown future fields.
- Preserve the raw evidence SHA-256 as provenance without embedding raw JSON in Git history.

## Verification boundary

Pre-version implementation head `b574ab7ffd0c15cf50dfe6c5af1b216a979e2616` passed Product Security `37096061393`, Updater Publication `37096061412`, and independent Heaven release verification/build. Because the synchronized v8.8.81 metadata changes release inputs, the final exact head requires fresh verification before integration.

## Unresolved risks and next work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Any continuation of stale #602/#603 or other v8.8.81 branches must rebase on canonical main and take a later patch version.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and the durable-evidence privacy boundary. The successor **must propagate** this obligation onward. **Do not break the chain.**
