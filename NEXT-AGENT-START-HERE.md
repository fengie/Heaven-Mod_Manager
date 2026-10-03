# v8.8.84 updater test-state isolation — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #671 / PR #672

## v8.8.84 behavior

- The updater integration-test classes that share `UpdatePackageStager.GetUpdaterRoot()` and canonical `pending-update.json` state are in one non-parallel xUnit collection.
- Unrelated integration tests remain parallel, and the existing per-test updater directories remain unchanged.
- Production updater topology, pending-state validation, recovery, and publication behavior are unchanged.

## Verification boundary

v8.8.83 source `ed7006e18d1fdbbdedd2209d4b766b5db938add9` remains the last closed hosted-Windows verification (run `37108647747`). It does not verify v8.8.84 because v8.8.84 changes test and release-input metadata.

PR #672 previously passed Workflow Feature PR Gate run `37122995254` on source-only head `b12d9eeb31b9e069d63a9983b26fe1e0dc80401c`. That evidence is historical after the v8.8.84 metadata/continuity synchronization. Require a fresh exact-final-head gate before integration.

## Execution/offload note

During this integration pass, the canonical `heaven` Bridge heartbeat was healthy but memory-limited to zero worker start slots (2.48 GiB available, 92% load). `heaven2` was healthy, but the protected cloud request ingress did not yet expose Agent Control; that capability is pending Toolbox PR #248. The senior integrator therefore used the authenticated GitHub path and did not weaken HMAC, proxy through heaven, or fabricate a local-agent dispatch.

## Unresolved risk

The final v8.8.84 candidate still requires fresh exact-head Workflow Feature verification after continuity/version synchronization; prior green source-only evidence must not be inherited.

## Next action

Run/observe the exact-final-head Workflow Feature PR Gate for PR #672. Merge only that exact green head, verify remote `main`, then reconcile every remaining PR against the new v8.8.84 baseline before assigning later patch numbers.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, updater topology/storage-retention invariants, filesystem containment, and durable-evidence privacy. The successor **must propagate** this continuity obligation to the **next agent after them**, and require that next agent to preserve and recursively propagate the same rules to their successor. **Do not break the chain.**
