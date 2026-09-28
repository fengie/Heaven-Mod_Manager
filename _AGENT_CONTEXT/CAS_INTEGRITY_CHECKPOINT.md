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

## Local full-gate checkpoint

Exact source 8334d725f6bdb73661e2f3ed71a04189db76950b passed both local Windows gates; evidence is EVIDENCE/cas-integrity-local-windows.log. The 25-stage verifier passed, including 88 integration tests and 11 self-tests; release ReadyToRun publishing passed. Generated caches were promoted only by the successful normal pipeline. Hosted canonical verification remains pending.


## Hosted failure and concurrency repair candidate

Hosted Windows Release Gate `36366784304` ran exact canonical commit `797991231819d8e5693efd671e5352fd447902a0`. The repository verification gate passed, but the release-build integration suite finished 87/88 because `BlobIntegrityTests.Concurrent_valid_captures_converge_and_restore_the_expected_bytes` hit `ERROR_SHARING_VIOLATION` opening the newly published hash-named object in `BlobStore.VerifyExistingAsync`.

Root cause: concurrent capture losers correctly refuse to trust a filename after their `File.Move(temp, dest, false)` loses the race, but Windows can briefly expose the destination pathname while the winning rename still owns delete/rename access. `VerifyExistingAsync` intentionally opens with `FileShare.Read`, so that transient rename handle can reject the immediate read open. Changing verification to share delete/write would weaken the stable-object integrity boundary and is not accepted.

Repair candidate production commit `3810c6b8c5baf1f7aff3b22952e137796dddaa8f` keeps `FileShare.Read` and adds a bounded, cancellation-aware exponential backoff only for Win32 sharing/lock violations (32/33). Hash mismatch remains `InvalidDataException` and is never retried; other I/O failures still surface normally. Test commit `cde16cc7db8a9f0fa2470797a4ee8c9d75c475a3` strengthens the same-digest race to 32 callers across 6 publication repetitions, verifies the surviving CAS object's SHA-256, asserts exactly one CAS file and no capture staging, restores and checks bytes, and adds canceled-capture staging cleanup coverage.

The earlier local Windows green evidence applies to production source `8334d725f6bdb73661e2f3ed71a04189db76950b`, not this repair candidate. Fresh focused tests, full local Windows verification/build, and an exact hosted Windows Release Gate are required before closure. Do not manually promote caches.
