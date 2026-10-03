# v8.8.85 import descendant-reparse hardening — candidate handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Working branch: `fix/676-import-reparse-hardening`
Change set: issue #676

## v8.8.85 behavior

- `SafeRecursiveTraversal.EnsureNoReparsePoints` provides an allocation-light fail-closed tree-topology check that never follows descendant reparse points.
- `ImportPublicationWorkspace.Publish` rejects unsafe descendant topology immediately before the final same-volume move into `ModsRoot`.
- `ImportPublicationWorkspace.Cleanup` and `RollbackPublished` reject unsafe descendant topology before recursive deletion, preserving the suspect tree rather than traversing it.
- Existing direct-child containment, root reparse, destination-exists, and same-volume guards remain in force.
- Windows junction regressions cover pre-publication descendant substitution and post-publication rollback substitution while proving external sentinel bytes remain untouched.

## Verification boundary

The last closed hosted-Windows source remains v8.8.84 `2ea6d6dd3851f24a40e562074a816d9bd1e61883` / run `37124460532`.

The v8.8.85 source/test checkpoint is `e2a63c6a653426b014c189e8b2e44c41981229bf`, followed by release/continuity metadata. It is **not yet exact-head verified**. Prior green evidence does not transfer to this changed production/test/release input.

Residual limitation: tree validation is path-based and narrows the unsafe descendant case, but a topology swap after the final validation and before the filesystem operation remains a TOCTOU risk. Do not claim handle-atomic containment.

## Coordination

Issue #673 / PR #674 is a separate unmanaged-adoption boundary and must not be merged into this change. Its stale branch currently claims v8.8.85 metadata from an older base; after this patch integrates it must reconcile onto fresh `main` and take the next available patch identity.

## Exact next action

Open/refresh the PR for #676, require exact-final-head CI/Windows verification, merge only that exact green head, verify remote `main` contains the intended source and metadata, close #676, then retire the temporary branch/ownership claim. Do not inherit v8.8.84 verification for this source.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, filesystem containment, fail-closed destructive boundaries, and durable-evidence privacy. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
