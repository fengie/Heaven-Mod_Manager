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
