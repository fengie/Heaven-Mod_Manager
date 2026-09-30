# CI / Build / Release Engineering

## CI is evidence, not ceremony
A green workflow matters only if it runs the checks that protect the relevant boundary on the exact source being accepted.

When CI persists verification state back into the repository, keep that state bound to the exact source revision that produced it. If the canonical branch advances while an older exact-input run is still finishing, preserve the older run's evidence as an artifact and skip stale repository write-back rather than rebasing or replaying it onto newer source. A non-fast-forward caused solely by that source advance is stale-bookkeeping contention, not a product failure; other push failures must still fail loudly.

## Build doctrine
- Pin or record important toolchain versions.
- Keep build inputs explicit and reproducible.
- Treat warnings/analyzer failures according to project policy; do not suppress them merely for green status.
- Cache only when invalidation semantics are understood and auditable.

## Test doctrine
CI should run the authoritative tests for changed boundaries. Preserve failure logs and enough metadata to reproduce the run.

## Release artifact doctrine
Verify the artifact users actually receive:
- source revision;
- configuration/platform;
- produced files;
- checksums/signatures where appropriate;
- dependency set;
- version metadata;
- installer/archive/update payload behavior.

A successful compile is not proof that packaging produced the intended artifact.

## Versioning
Version changes must be consistent across the product, package, update metadata, and release notes. Avoid implicit version sources that can drift.

## Update systems
Treat updating as a migration: verify authenticity/integrity, stage before replace, preserve rollback, define interruption behavior, avoid half-published state, and test old→new plus failure/retry paths.

## Dependency integrity
Lock or validate dependencies where reproducibility/security requires it. Detect unexpected source or binary substitution.

## Release validation
Before publication:
1. build from the intended revision;
2. run required tests/gates;
3. verify artifact identity and contents;
4. smoke-test installation/launch/update where applicable;
5. confirm rollback/recovery path;
6. preserve evidence.

## Post-release
Perform lightweight health checks appropriate to the product and preserve a way to map user reports back to exact release artifacts.

## Reusable lesson rule
When CI/release fails for a new systemic reason, fix the immediate issue and decide whether the doctrine, gate, or regression suite should change so the class of failure is caught earlier.

## Canonical installed/runtime identity
A development build, validation process, staging deployment, old shortcut target, or version-stamped folder name is not proof of what users actually run. Launch through the canonical user path/updater-managed launcher, assert executable/process/artifact identity and version/build metadata, distinguish validation from installed instances, and clean up temporary validation clients when their purpose ends.

## Delivery completion policy
When project policy defines publication/deployment as part of version completion, “implemented” or “merged” is not “released.” Completion requires release gates, publication, canonical distribution identity, and post-publication health/identity verification unless an explicit hold exists.

## Supply-chain and privileged-runner security

- Pin every third-party CI action to a full immutable commit SHA. Human-readable release tags may appear only as comments/documentation because tags can move.
- Give workflow tokens the smallest explicit permissions required. A write-capable workflow is a security boundary, not a convenience.
- Persistent self-hosted runners must not execute fork pull-request code. Gate PR jobs to trusted same-repository heads, and use ephemeral/sandboxed infrastructure when genuinely untrusted code must run.
- A privileged release job must not fetch “latest” tools or trust arbitrary preinstalled executables. Use an independently pinned and cryptographically verified bootstrap or a separately trusted immutable tool image.
- Do not disable dependency vulnerability auditing just to keep CI green. A newly reported vulnerable dependency is a release input change that must be triaged.
- Software-update integrity should ultimately authenticate publisher identity independently of the repository/release account. Hashes stored beside an artifact detect corruption but do not alone survive compromise of the publication authority.
- Encode these invariants in executable CI policy tests so later workflow edits fail closed.

## Consumer-feed-first multi-repository releases

When one logical release spans multiple repositories or distribution surfaces, order visibility by consumer dependency. The surface production clients actually poll must become ready and verified before a secondary human-facing/canonical listing becomes visible. Re-check the canonical source after asset upload and immediately before publication, because upload is a race window. Do not use blanket `cancel-in-progress: true` around the transaction. Make retry idempotent by recovering automation-owned drafts, fail closed when drift cannot be completely classified, and finish with cross-surface immutable-asset parity verification.

Protect release-order invariants from stale branch integration in at least one independent CI policy outside the release-specific test itself. Before merging branches that touch release/CI policy, require reconciliation with current main and rerun checks on the exact reconciled SHA; an older authored commit integrated later can otherwise restore stale whole-file state without an explicit revert.
