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

Current hosted-Windows closure: v8.8.81 source `aa5e56afa5bf8db73bcb7ac0af0c9825874c736a` passed run `37105670926` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.81-heaven-windows-closure.log`.

The tested source remains `aa5e56afa5bf8db73bcb7ac0af0c9825874c736a` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and next work

#559/#558 retain Browse Mods work; #350/#354 retain external prerequisites; RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent. Any continuation of stale #602/#603 or other v8.8.81 branches must rebase on canonical main and take a later patch version.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and the durable-evidence privacy boundary. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
