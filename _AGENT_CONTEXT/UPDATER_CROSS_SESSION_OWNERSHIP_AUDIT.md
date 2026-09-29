# Updater cross-session single-writer ownership audit — 2026-09-28

## Canonical source inspected

- Repository: `fengie/mhw-mods`
- Canonical branch: `main`
- Canonical HEAD inspected immediately before this audit: `a83dc6e047ccf98e896f10c25772df99b95426d1`
- Support branch: `agent/support-updater-cross-session-ownership-audit-20260928`
- Scope: updater apply ownership/synchronization only.
- Production C#, updater behavior, tests, workflows, release scripts, and verification caches are deliberately unchanged by this checkpoint.

This is independent of the open updater publication/media/WPF/metadata support PRs visible at audit start. It does not reopen their boundaries.

## Selected question

Can the updater prove that at most one helper can mutate a particular installation at a time across every Windows session that can reach that installation?

This was selected because the updater now has durable rollback/recovery semantics, but those semantics assume one writer owns the install/journal transition. A single-writer guard is therefore an integrity boundary, not a cosmetic multi-instance feature.

## Source inspected

- `src/MhwModManager.Updater/UpdateRuntime.cs`
  - `UpdateMutexLease.Acquire`
- `src/MhwModManager.Updater.Helper/Program.cs`
  - helper acquisition order and lease lifetime
- `tests/MhwModManager.IntegrationTests/UpdateRuntimeTests.cs`
  - `Concurrent_update_attempt_is_serialized`
- `src/MhwModManager.Updater/UpdateInstaller.cs`
  - apply/rollback behavior protected by the helper lease
- existing updater release/security support guidance, including the requirement for explicit multi-instance ownership

## Existing strengths

1. The helper acquires the update lease before waiting for the old application process and keeps it for the entire apply / launch-health / confirm-or-rollback path.
2. The lock identity is derived from a normalized installation path hash, so independent installs in the same session do not block each other.
3. The current integration test proves that a second acquisition for the same installation in the same Windows session fails closed with `IOException`.
4. The installer already has durable recovery state. A stronger ownership primitive can therefore be introduced without redesigning update journaling.

## Confirmed finding

### P1 — `Local\` updater semaphore does not serialize the same installation across Windows sessions

**Confirmed repository behavior**

`UpdateMutexLease.Acquire` constructs:

```text
Local\MHWMM.Update.<install-root-hash>
```

with `System.Threading.Semaphore`.

`MhwModManager.Updater.Helper.Program.Main` acquires that lease with a zero timeout before touching updater recovery/apply state.

The only current regression, `Concurrent_update_attempt_is_serialized`, performs both acquisitions inside the same test process/session.

**Authoritative Windows behavior**

Microsoft documents that named semaphores/mutexes/events have separate per-session namespaces. The `Local\` prefix explicitly selects the current session namespace. `Global\` selects the global namespace and can be used to detect/coordinate instances across client sessions.

Primary references:

- https://learn.microsoft.com/windows/win32/termserv/kernel-object-namespaces
- https://learn.microsoft.com/dotnet/api/system.threading.semaphore.-ctor

Microsoft also documents that the special privilege requirement for creating global objects is limited to file-mapping and symbolic-link objects; it is not a general prohibition on creating a global semaphore.

**Consequence**

Two helpers in different interactive sessions can both successfully acquire different `Local\MHWMM.Update.<same-hash>` semaphore objects for the same physical installation.

If both sessions can write that installation, both helpers can then enter the update transaction concurrently. That violates the updater's single-writer assumption and can interleave:

- backup creation;
- journal phase changes;
- product-file replacement/removal;
- install-marker/product-manifest publication;
- startup-health confirmation;
- rollback/restoration.

The updater's rollback logic is designed for one transaction owner. It is not a multi-writer transaction protocol.

This audit does **not** claim a reproduced corrupted install. The namespace gap itself is confirmed by source plus Microsoft platform documentation; the exact failure interleaving remains to be runtime-characterized.

## Why the existing test is insufficient

`Concurrent_update_attempt_is_serialized` proves only:

> two callers in the same Windows session share the same named semaphore.

It does not prove:

> every process capable of mutating the same installation shares one ownership primitive.

Those are different properties on Windows because Fast User Switching / Remote Desktop sessions have separate local kernel-object namespaces.

## Recommended narrow implementation checkpoint

Keep the implementation boundary limited to updater ownership.

### Minimum supported-model fix

If the supported installation model is "one Windows account may have multiple sessions", make the install lock cross-session, for example by using a `Global\` named synchronization object derived from the same install identity.

Do not silently catch access/security failures and continue unlocked. Failure to establish the required ownership primitive must fail closed before any updater mutation.

### If shared installs across different Windows accounts are supported

Define the security model explicitly. A global named object uses Windows object security; cross-account access may depend on its ACL.

A robust alternative/supplement is a held filesystem lock whose identity is anchored to the installation itself (for example, a dedicated lock file opened for exclusive sharing for the lifetime of the helper). If this route is chosen, it must obey the existing filesystem/reparse/ownership rules and must not create a new unsafe cleanup race.

Do not broaden the checkpoint into general privilege/elevation redesign.

### Primitive choice

A `Mutex` is worth evaluating instead of `Semaphore(1,1,...)` because a mutex models single ownership and exposes abandonment semantics after an owner dies. That is particularly relevant to a recovery-oriented updater. Do not change primitives solely for style; first pin the desired crash/restart behavior in tests.

## Required regression / fault coverage

Before closing a production fix, add focused tests that prove the intended ownership contract:

1. **Name/scope policy:** the production lock is explicitly cross-session for the supported Windows model (or uses a filesystem lock that is not session-scoped).
2. **Same-session contention:** preserve the existing second-helper rejection.
3. **Cross-process contention:** separate processes targeting one install cannot both acquire.
4. **Crash recovery:** killing the owner cannot leave the installation permanently un-updatable; the next helper either acquires safely or receives a deterministic recoverable ownership state.
5. **Different installs:** unrelated installation roots do not block one another.
6. **Security failure:** inability to establish the cross-session ownership primitive fails before backup/journal/live-file mutation.
7. **Recovery integration:** a helper that acquires after a crashed predecessor must still obey the existing journal and rollback state instead of assuming a fresh update.

A true multi-session Windows fixture is the strongest proof. If hosted CI cannot create a second interactive session, preserve that limitation explicitly and combine cross-process automated coverage with an independent Windows multi-session manual/runtime checkpoint.

## Deliberately not changed

- No updater C#.
- No test code.
- No release/publication scripts.
- No updater manifest protocol.
- No WPF shutdown/handoff behavior.
- No journal schema.
- No rollback/native-replacement behavior.
- No verification cache.

This remains a documentation/research checkpoint because changing the lock without Windows ownership/security characterization would widen the risk boundary while other updater work is active.

## Verification actually performed

- Re-checked canonical GitHub `main`; HEAD remained `a83dc6e047ccf98e896f10c25772df99b95426d1` immediately before branch creation.
- Inspected current updater runtime/helper/test source listed above.
- Inspected open updater PR/branch inventory; no current PR was dedicated to cross-session updater ownership.
- Compared the implementation to Microsoft's current named-kernel-object namespace documentation.
- No product tests/builds were run by this support audit.
- No Windows multi-session runtime reproduction was performed.
- No verification cache was promoted.

## Severity and confidence

- **Severity:** P1 reliability/integrity boundary if a writable installation is reachable from multiple Windows sessions.
- **Confidence that the current lock is session-scoped:** high / confirmed by source and Microsoft documentation.
- **Confidence in a specific corruption sequence:** not claimed; requires runtime fault/interleaving characterization.

## Recommended next independent checkpoint

After the currently owned updater publication/integration work settles, implement and Windows-verify one narrow cross-session single-writer fix. Keep it separate from release publication, HTTP transport, WPF lifetime, metadata rollback, and end-to-end old→new validation.

## Success criteria for that future checkpoint

- all helpers targeting one installation share one lock domain across supported sessions;
- lock establishment failure is fail-closed before mutation;
- crash/abandon behavior is deterministic and tested;
- existing journal recovery remains authoritative;
- focused Windows tests pass;
- the exact changed production source earns the repository's full required verification/release gate before closure.

## Successor handoff

Preserve the permanent continuity constitution and active Learned Rules. Treat this audit as the specialized authority for updater cross-session ownership until superseded by a narrower implementation/verification record.

The successor must preserve and recursively propagate the same continuity obligation to the agent after them. Do not break the chain.
