# CAS integrity checkpoint — 2026-09-27

Base: canonical main 23ddc6a9dc788bf864e84161c41ea3283a4fcef7. Work branch: agent/cas-integrity-20260927.

## Implemented contract

Capture hashes any existing hash-named object (including a concurrent publication winner) before discarding its verified temporary copy or inserting a blobs row. Mismatch throws InvalidDataException. No automatic overwrite/quarantine/repair is attempted: preserve the corrupt artifact and source for deliberate recovery.

Restore supplies the advertised SHA to AtomicFileOps. After the copy and durable flush, AtomicFileOps hashes its private staged file through the still-exclusive ReadWrite handle. It refuses publication on mismatch. This validates the actual bytes to be published, avoiding a source-hash-then-reopen gap. Native replacement and 1176/1177 recovery-material preservation remain unchanged.

The AtomicFileOps signature now puts CancellationToken last; the only repository caller is BlobStore and was migrated. A strict baseline build on this Windows SDK reproduced CA1068 in the prior agent's signature despite historical hosted green claims. The final gate must run with normal warning enforcement.

## Tests and evidence

New BlobIntegrityTests cover unregistered/registered corrupt recapture, same-length corruption, restore to existing/absent targets, valid concurrent capture, uppercase digest, corrupt deployment, corrupt startup-recovery material, repair-and-retry, partial multi-file rollback, and canceled restore cleanup.

Baseline characterization: strict build failed CA1068. A diagnostic build with TreatWarningsAsErrors=false allowed the original production code to execute; 6 of 7 focused tests failed (including committed corruption and silent corrupt recovery). This is characterization only, not a weakened release gate.

After fix: dotnet run --project tests/MhwModManager.IntegrationTests -c Release -- -class '*BlobIntegrityTests': 9/9 PASS, normal strict build. Full local and hosted gates pending at this checkpoint.

## Reviewed other-agent work

Fetched all origin refs. Support audit heads match the inventories in SUPPORT_BRANCH_INTEGRATION_2026-09-27.md; no newly unintegrated support implementation was found. Rechecked CAS findings against actual BlobStore/DeploymentExecutor/ModScanner callers and reproduced them. Reviewed native replacement diff 74ba0a9..6d52ede and existing recovery tests; retained that design and corrected its analyzer-invalid signature.

Smart Pack head ddcbe89 adds a separate 602-line planner plus tests; deferred, not accepted or rejected on correctness. Older workflow head cae2407 spans 55 files and deployment/storage/UI changes; deferred pending its own review and exact verification. No stale branch-local continuity snapshots were merged.

## Invariants and limits

Journal/transaction ownership, destination containment, native failure handling, source immutability, and fail-closed RecoveryRequired are preserved. A recovery failure leaves the live after-image intact; retry succeeds once independently correct before-image material is restored.

This boundary validates content, not filesystem identity. CAS root/reparse/hardlink policy, hostile topology swaps, scanner cached metadata, and migration convergence remain separate work. Existing-object validation is a point-in-time check; every restore independently checks staged content. Staging can create parent directories but does not publish corrupt destination bytes. Verification adds one staged-file read per restore and one existing-object read per duplicate capture; measure before optimizing. No verification cache is manually promoted.

## Next work and support tasks

Close the exact full Windows gate before another production boundary. Then prioritize recursive ModScanner/adoption/Smart Inbox reparse traversal, independently from CAS.

Support tasks: (1) measure duplicate-capture/restore hashing at realistic blob sizes, preserving validation; (2) audit CAS root/leaf reparse policy and design real-link fixtures without changing migration semantics; (3) reconcile obsolete historical top-level guidance in README_FIRST/KNOWN_ISSUES against current closure evidence without deleting history. Each task must document scope, commands, and limitations; production changes require their own gate.

The successor must read AGENTS.md, inherit and preserve the permanent continuity constitution and active Learned Rules, update durable handoff state, and explicitly require its successor to recursively propagate these obligations to the agent after them. Do not break the chain.
