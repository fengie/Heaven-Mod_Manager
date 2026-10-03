# v8.8.84 unmanaged-adoption hardening — candidate handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #673 / PR #674

## Candidate behavior

- Each adoption invocation owns a GUID-backed package directory, eliminating check-then-act directory sharing and cross-run destructive cleanup.
- Candidate sources are canonicalized beneath the configured live root and every source component is rejected if it is a reparse point at the copy boundary.
- Source topology is checked again after copying and the owned copy must hash-identically to the discovery candidate before DB publication.
- Failure cleanup remains scoped to the invocation-owned package; adoption DB writes remain transactional.

## Verification boundary

This candidate changes product source after the v8.8.83 tested source, so prior closure evidence cannot be inherited. PR #674 requires fresh exact-head verification before integration. No local Heaven execution surface is exposed in the originating chat, so no local test result is claimed.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; preserve exact-input verification, filesystem containment/no-follow boundaries, updater topology/storage-retention boundaries, and durable-evidence privacy. Propagate this obligation recursively to the next agent. Do not break the chain.
