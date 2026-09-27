# Closed implementation checkpoint — live DeploymentExecutor physical containment

The first production checkpoint from this audit is **CLOSED and hosted-Windows verified**.

- exact verified merge: `356fde242046b78e39c7266c57b27e52220141fa`
- hosted Windows Release Gate: `36341049469`
- evidence/cache persistence: `dc7eb83c94427479c59413c77935050dadf051ff`
- repository verification: **25/25**
- functions: **611/611**
- explicit call sites: **6478 / 0 uncovered**
- integration/fault injection: **76/76**
- ReadyToRun publish: PASS
- release SHA-256: `F7CBC330D652835FFBC6A24395D105FF509F3800FE21E741FBDD9BAE7D94433D`

The closed scope is descendant-reparse containment for live `DeploymentExecutor` paths: preparation/preconditions, immediate mutation, rollback/startup recovery, pruning, and lock-related path inspection. Real Windows junction tests cover Add/Replace/Remove and restart recovery.

The configured game root is still the trust anchor. Descendant reparse points fail closed. The remaining topology-swap TOCTOU after the final path check is documented rather than hidden.

The next recommended checkpoint from this audit is the separately scoped **`ReplaceFileW` failure-postcondition characterization** under LR-003. CAS trust and recursive source/adoption/Inbox traversal remain later independent boundaries.

---


# Windows filesystem safety deep audit — 2026-09-27

## 1. Scope and canonical state

This is a documentation-only support checkpoint focused on Windows filesystem containment, native replacement failure semantics, content-addressed storage integrity, recursive traversal, rollback/recovery, and path edge cases.

Audit branch:

`agent/windows-filesystem-safety-audit-20260927`

Branch base / canonical `main` when the branch was created:

`6ada5a5c4cc83afadfba42bc6af6559540920e3d`

The immediately preceding PlannerSnapshotRepository boundary is already CLOSED and Windows verified:

- verified source: `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`
- hosted Windows Release Gate: `36336190920`
- evidence/cache persistence: `852f07b9d6ad0457c161df0aa1c8165981d349cf`
- repository verifier: **25/25**
- production fingerprints: **610/610**
- explicit call sites: **6456 / 0 uncovered**
- Core: **79/79**
- Automation: **20/20**
- Integration/fault injection: **72/72**
- self-test: **11/11**
- App win-x64 compile/analyzers: PASS
- ReadyToRun publish: PASS

Therefore this audit does not overlap an unresolved production verification boundary.

The GitHub connector used for this support task exposes canonical remote repository state but no local working tree, so there is no local `git status` result to report. Private-repository GitHub code-search indexing was incomplete (queries returned zero/incomplete results), so this audit used the recursive repository tree, direct source reads, recent commit/diff inspection, tests, continuity documents, and Microsoft primary documentation rather than treating indexed search as authoritative.

Parallel work to preserve:

- `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`
- `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`
- open support PR #4, which independently proposed a broader test-gap/performance audit but is not canonical main at this checkpoint

Where this audit overlaps database transaction topics, the deep SQLite audit remains the more specific authority. This document narrows to filesystem semantics.

---

## 2. Executive findings

### P0 — physical containment is not enforced for live deployment

`PathRules.Normalize` protects the logical key against rooted paths, `..`, ADS syntax, trailing spaces/dots, and common DOS device names. `DeploymentExecutor.Destination`, however, then combines that validated logical key with `gameRoot` and performs ordinary path-based Windows filesystem operations.

There is no live-deployment equivalent of `ArchiveInspector.EnsureNoReparsePoint`.

An existing directory junction/symbolic link below `gameRoot` or `nativePC` can therefore redirect:

- Add / Replace;
- Remove;
- rollback restore;
- startup recovery restore;
- hashing/precondition reads;
- empty-directory pruning traversal

to a physical location outside the intended game root even though the lexical path still begins with the correct root.

This is the highest-priority containment gap.

### P0 — recursive source/live scans follow reparse points

Several services use `Directory.EnumerateFiles(..., SearchOption.AllDirectories)` or the equivalent recursive directory enumeration without disabling reparse recursion:

- `ModScanner`
- `UnmanagedAdoptionService`
- `SmartInboxService.CopyDirectoryAsync`
- Smart Inbox category enumeration
- other less safety-critical recursive visual/metadata scans

Microsoft/.NET documentation states that `SearchOption.AllDirectories` includes reparse points such as mounted drives and symbolic links and can loop indefinitely when a link creates a cycle.

Consequences include:

- scanner can read/hash files outside a mod source package through a junction;
- external content can be captured into CAS and modeled as if it lived under the mod package;
- unmanaged adoption can read/hash/copy files outside the live mod root;
- direct Smart Inbox directory import can copy outside-tree content into the managed Mods library;
- recursive scans can become unbounded or cycle.

This is not merely path-normalization theory: the source explicitly requests recursive traversal and does not exclude reparse points.

### P0/P1 — `ReplaceFileW` failure is not equivalent to “nothing changed”

`AtomicFileOps.ReplaceFromAsync` stages a temp file in the destination directory, calls `ReplaceFileW`, throws when the native API returns false, and then unconditionally tries to delete the temp path in `finally`.

Microsoft documents `ReplaceFileW` errors where a false return can occur after names, streams, security descriptors, or attributes have already changed.

Of particular importance with the current `backup = null` call:

- `ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176)`: Microsoft documents that the replaced file no longer exists while the replacement file remains under its original name.
- `ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 (1177)`: the replacement may remain under its original name with inherited streams/attributes while the replaced file exists under a different name.
- `ERROR_UNABLE_TO_REMOVE_REPLACED (1175)`: names remain unchanged.
- other failures can still leave the replacement with inherited metadata even if names remain.

For the 1176 postcondition, the current `finally` may delete the remaining replacement temp after the original destination pathname has disappeared. `DeploymentExecutor` will then attempt rollback; a missing destination does not match a non-null expected after-image, so normal recovery can fail closed into `RecoveryRequired` instead of restoring the before blob automatically.

This is a source + documented-OS-semantics risk. It was **not reproduced at runtime in this audit**.

### P0/P1 — CAS object identity is weaker than the SHA name implies

`BlobStore` computes SHA-256 while copying source bytes into a private temp. But:

- if a destination named by that SHA already exists, the newly verified temp is deleted without re-hashing the existing object;
- a racing writer that wins the destination path is trusted if the path merely exists;
- `RestoreAsync` checks only `File.Exists(source)`;
- no check rejects a reparse-point object at the hash path;
- no check rejects a reparse-point CAS root;
- no file-identity/hardlink policy exists;
- migration can intentionally create a hardlink from the new CAS to the legacy v7 blob.

Therefore a file whose *name* is a valid SHA is not necessarily proven to still contain those bytes at restore time.

### P1 — legacy migration hardlinks weaken “immutable CAS” unless aliases are controlled

`LegacyV7Migrator.EnsureLegacyBlob` tries `CreateHardLinkW(dst, src)` on Windows before falling back to `File.Copy`.

A hardlink is another pathname for the same file data. Mutation through any hardlink alias changes the bytes visible through every alias.

The migration verifies referenced CAS hashes before declaring migration success, which is a strong final check. But the hardlinked CAS remains physically aliased to the legacy blob after that verification. Unless every alias is thereafter immutable by policy/enforcement, the CAS invariant “hash-named object bytes cannot change” is weaker than the class documentation suggests.

This is a design/invariant gap, not evidence that legacy v7 actually mutates blobs after migration.

### P1 — archive extraction is stronger than live deployment, but still has a race

`ArchiveInspector.ExtractSafely` is the strongest physical-containment code in the repository:

- lexical path validation;
- full-path root containment;
- destination-root reparse rejection;
- parent-component reparse walk before each write;
- entry-count and expanded-byte limits;
- no overwrite.

However, the parent check and the subsequent write are separate path-based operations. Another actor with filesystem access can replace a checked parent with a junction between validation and `WriteToFile`.

The first implementation checkpoint should preserve the existing check and add Windows tests. Closing the final race may require handle-based identity/containment or a narrower trusted-directory ownership model.

### P1/P2 — DOS device-name protection is incomplete

The reserved-name set contains:

- CON, PRN, AUX, NUL
- COM1–COM9
- LPT1–LPT9

but `IsUnsafeSegment` uses `Path.GetFileNameWithoutExtension`.

Microsoft documents that reserved DOS names remain reserved with **any extension**, for example `NUL.txt` and `NUL.tar.gz`. For `NUL.tar.gz`, `Path.GetFileNameWithoutExtension` yields `NUL.tar`, so the current validator can miss it.

Microsoft also documents the superscript-digit aliases:

- COM¹, COM², COM³
- LPT¹, LPT², LPT³

which are not in the current set.

These cases should be pinned with pure unit tests before a production fix.

### P2 — long-path behavior is inconsistent at the native `ReplaceFileW` seam

The WPF app manifest does not declare `longPathAware`.

Microsoft documents that long-path removal for affected Win32 APIs requires both:

- the Windows registry policy; and
- an application manifest with `longPathAware=true`.

`ReplaceFileW` is one of the Win32 APIs affected by the long-path rules.

The managed .NET path/copy code may therefore successfully reach paths that the direct native `ReplaceFileW` call later rejects around MAX_PATH depending on environment/runtime behavior.

This is primarily compatibility/robustness, not a root-escape finding.

### P2 — managed byte identity does not include alternate streams/metadata

Logical managed paths reject `:`, so the manager does not intentionally deploy ADS paths.

However, Windows files can have named streams, and Microsoft documents that `ReplaceFileW` can merge/preserve stream and metadata behavior from the replaced file. `HashingService` hashes the default data stream only.

Therefore “live file matches before/after SHA-256” does not prove every ADS/security/metadata property is unchanged.

The project should explicitly decide whether those are outside the managed-state contract. If they are outside scope, document it rather than accidentally implying full Windows file-object identity.

---

## 3. Required physical-containment invariant

Recommended invariant for every live-file mutation and every recursive import/adoption traversal:

> A logical managed path must be lexically valid **and** its filesystem resolution must remain inside the intended trusted root without traversing an unsupported reparse point. Validation must be performed as close as practical to the mutation/read that depends on it. No destructive or import operation may rely on string-prefix containment alone as proof of physical containment.

For a high-assurance Windows implementation, a future guard should be designed around handles/file identity rather than only repeated `Path.GetFullPath` calls.

Potential primitives to evaluate in a dedicated implementation checkpoint:

- open root/parent components with reparse-aware flags;
- reject `FileAttributes.ReparsePoint` for unsupported components;
- use `GetFinalPathNameByHandleW` to inspect final resolved path;
- use `FILE_ID_INFO` (volume serial + file ID) where stable identity matters;
- if a race must be completely closed, perform the actual mutation via a handle-relative/handle-based operation rather than checking by path and then reopening by path.

A first fix does **not** need to redesign every filesystem operation at once. Tests should define the invariant first.

---

## 4. Filesystem-safety matrix

Legend:

- **Strong** — implementation has explicit protection plus relevant tests.
- **Partial** — useful protection exists but an important physical/race edge remains.
- **Lexical** — only logical/string containment is enforced.
- **Gap** — no relevant guard found.
- **N/A** — not meaningful for this operation.

| Operation | Intended root | Lexical containment | Physical/reparse containment | Hardlink policy | ADS/device handling | TOCTOU | Failure / recovery | Existing evidence | Risk |
|---|---|---|---|---|---|---|---|---|---|
| Deployment Add | game root / nativePC | Strong logical key | **Gap** | none | logical ADS rejected | parent topology can change | journal rollback, but same redirected path used | crash/preflight tests | **P0** |
| Deployment Replace | game root / nativePC | Strong logical key | **Gap** | none | ADS logical paths rejected; ReplaceFile metadata semantics external | precondition then path-based replace | rollback strong for normal byte states; native partial failure gap | strong crash tests | **P0** |
| Deployment Remove | game root / nativePC | Strong logical key | **Gap** | none | leaf symlink behavior OS-specific; parent junction redirects | per-path precondition then delete | rollback via CAS | strong normal recovery | **P0** |
| RestoreOriginal | game root / nativePC | Strong logical key | **Gap** | source CAS unverified at restore | logical ADS rejected | physical target can move | uses AtomicFileOps | ownership/undo test | **P0** |
| Rollback | game root / nativePC | Strong logical key | **Gap** | CAS trust gap | byte SHA only | re-resolves path at rollback | fail-closed if live byte unknown | interrupted rollback test | **P0/P1** |
| Startup recovery | game root / nativePC | Strong logical key | **Gap** | CAS trust gap | byte SHA only | topology may differ from crash time | fail-closed external-byte guard | recovery tests | **P0/P1** |
| Empty-dir pruning | nativePC or game root | full-path string prefix | **Gap** | N/A | N/A | parent chain can change | stops lexically at root | root-preservation test | **P1** |
| CAS capture | state BlobRoot | N/A | **Gap** root/object reparse | no rejection | N/A | existing object race trusted | temp cleanup | missing-blob rescan only | **P0/P1** |
| CAS restore | state BlobRoot -> live root | destination logical via caller | **Gap on source + target** | no rejection | N/A | no re-hash before restore | AtomicFileOps outer recovery | no corrupt-CAS test | **P0** |
| Archive inspect | archive | strong lexical | N/A | N/A | partial device check | archive metadata static | fail before extraction | traversal test | P1/P2 |
| Archive extract | Mods staging/root | strong | **Partial/strong** parent reparse walk | N/A | lexical ADS/device | check/write race | throws on unsafe path | traversal only | **P1** |
| Wrapper normalization | extracted staging | lexical local names | **Gap after extraction** | N/A | inherited from extracted FS | path-based move | no special recovery | none | P1/P2 |
| ModScanner | mod source | logical destination key | **Gap: AllDirectories follows reparse** | reads hardlink aliases normally | PathRules after enumeration | source topology can change | capture source metadata/hash checks | same-size/timestamp test | **P0** |
| Unmanaged adoption scan | live mod root | ManagedKey lexical | **Gap: AllDirectories follows reparse** | reads aliases | PathRules key | live topology changes | copy cleanup on normal failure | basic adoption test | **P0** |
| Smart Inbox directory import | Inbox -> Mods | relative copy | **Gap: AllDirectories follows reparse** | copies target bytes | destination key later scanned | source topology changes | per-item recoverable exception | directory import test | **P0/P1** |
| Smart Inbox archive import | Inbox -> Mods | ArchiveInspector | Partial/strong during extract | N/A | archive lexical guard | parent race | per-item failure | basic archive/import test | P1 |
| Duplicate cleanup move | mod source -> archive | no common containment guard found | source may itself be reparse | link semantics undefined by app | N/A | source can change | deep SQLite audit already flags move/DB gap | no crash test | P1 |
| Save backup copy | configured save -> state | configured explicit source | ordinary path semantics | follows alias | default stream only | source can mutate; FileShare.ReadWrite | disk snapshot then DB row | one happy-path test | P2 |
| Migration CAS import | legacy blobs -> new CAS | hash-named | destination root not reparse-checked | **hardlink intentionally attempted** | default stream hash | alias can mutate later | final SHA verification | no alias-mutation test | **P1** |
| Game profile validation | selected game root | Path.GetFullPath + prefix | **Gap: lexical only** | N/A | profile validator weaker than PathRules | root topology can change | profile rejected only on lexical invalidity | generic profile test | P1/P2 |
| Game registry atomic write | state root | internal path | no reparse root guard | N/A | N/A | state root could redirect | temp + File.Move | no adversarial root test | P2 |
| Nexus preview cache | state root | internal hash path | no reparse root/object guard | N/A | N/A | temp publish race | temp cleanup | static hardening guard only | P2 |
| Texture preview cache | state root | internal hash path | no reparse root guard | N/A | N/A | external converter output | overwrite/move | no adversarial root test | P2 |

---

## 5. ReplaceFileW deep dive

### Repository behavior

`AtomicFileOps.ReplaceFromAsync`:

1. creates the destination directory;
2. stages bytes into a unique temp file in the destination directory;
3. flushes managed buffers and requests an OS flush;
4. checks cancellation;
5. if destination exists on Windows, calls:
   `ReplaceFile(destination, temp, null, 0, 0, 0)`;
6. throws `Win32Exception` on false;
7. unconditionally attempts `File.Delete(temp)` in `finally`.

The same-directory staging is good: Microsoft requires replacement/replaced/backup operands of `ReplaceFileW` to be on the same volume, and the current native operands satisfy that requirement even if the original CAS source is on another volume.

### Documented failure postconditions that matter

Microsoft primary documentation:

https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-replacefilew

The documentation distinguishes failures after different portions of the replacement algorithm:

#### ERROR_UNABLE_TO_REMOVE_REPLACED (1175)

Names remain in their original states.

Likely application consequence: ordinary exception + rollback can reason about the expected before image.

Still needs a test because metadata/stream side effects must be checked rather than assumed absent.

#### ERROR_UNABLE_TO_MOVE_REPLACEMENT (1176)

With no backup file specified, Microsoft documents that the replaced file no longer exists and the replacement remains under its original name.

Mapped to this implementation:

- `destination` may be missing;
- `temp` may still contain the intended after bytes;
- method throws;
- `finally` sees temp and deletes it.

After returning to `DeploymentExecutor`:

- journal is durable;
- rollback inspects destination;
- missing destination does not equal non-null `AfterSha256`;
- `RestoreJournalRowAsync` treats it as neither before nor after;
- automatic rollback can enter `RecoveryRequired`.

The durable before blob may still exist, but the normal recovery state machine intentionally refuses to overwrite an unknown state. A special native-failure interpretation may be needed, but it must be driven by reproduced tests.

#### ERROR_UNABLE_TO_MOVE_REPLACEMENT_2 (1177)

Microsoft documents more complicated postconditions involving replacement data under the replacement name and the replaced file under another name, with streams/attributes potentially merged.

Because this implementation supplies no backup pathname, do not invent the exact unnamed temporary path that Windows may use. A Windows integration fixture must inspect actual postconditions.

#### Other errors

Microsoft states names can remain unchanged while replacement metadata/streams may already have been inherited.

Therefore the correct invariant is:

> `ReplaceFileW == false` does not imply “destination and temp are exactly unchanged.”

### Required test seam

Do not attempt to force these conditions only through random filesystem timing.

Create an isolated native-replacement abstraction in a future checkpoint, for example:

`IAtomicReplaceBackend.Replace(replaced, replacement, backup)`

Production backend calls `ReplaceFileW`.

A Windows integration/native-fixture backend can either:

1. reproduce real Win32 error conditions where practical; and/or
2. deliberately materialize the Microsoft-documented postcondition on disk and then return the matching error code.

The test must assert filesystem bytes/paths, not only an exception code.

### Exact proposed tests

#### `Atomic_replace_1175_failure_preserves_recoverable_before_image`

Fixture:
- destination = BEFORE;
- staged replacement = AFTER;
- backend materializes documented 1175 postcondition.

Action:
- call replacement through DeploymentExecutor Replace.

Expected filesystem:
- destination still BEFORE;
- no external path modified;
- any temp cleanup must not destroy a needed recovery object.

Expected DB:
- operation ultimately RolledBack;
- manifest/original/mod-state equal before image;
- no Committed marker.

Windows-specific: yes.

#### `Atomic_replace_1176_failure_does_not_discard_only_recovery_material`

Fixture:
- destination = BEFORE;
- replacement = AFTER;
- backend materializes documented 1176 postcondition: destination absent, replacement remains.

Action:
- fail Replace and let executor rollback.

Expected filesystem:
- final state must be exact BEFORE **or** operation must remain RecoveryRequired with every recoverable byte preserved;
- never end with both destination and replacement recovery material deleted solely because false was returned.

Expected DB:
- if rollback succeeds: RolledBack and before metadata;
- otherwise RecoveryRequired, not Committed.

Windows-specific: yes.

#### `Atomic_replace_1177_failure_preserves_documented_postcondition_for_recovery`

Fixture:
- materialize 1177 documented state.

Expected:
- recovery code identifies known bytes/file identities without destructive guessing;
- no automatic deletion of the only copy of before/after content.

Windows-specific: yes.

#### `Atomic_replace_other_failure_does_not_assume_stream_metadata_unchanged`

If ADS/metadata are in scope, assert them. If they are explicitly out of managed-state scope, document that contract and assert default data-stream recovery only.

---

## 6. Reparse / junction containment deep dive

### Live deployment

Example:

`<gameRoot>\nativePC\redirect\victim.bin`

where:

`redirect -> C:\outside-target`

Logical key:

`nativePC\redirect\victim.bin`

passes current `PathRules.Normalize`.

`Destination` returns a lexical path under `gameRoot`.

Ordinary Windows path-based operations then resolve the parent junction and operate on the target location.

Affected code paths:

- `PrepareChangesAsync` existence/capture;
- `VerifyPreconditionAsync` hash;
- Add/Replace via BlobStore + AtomicFileOps;
- Remove via `File.Delete`;
- rollback `RestoreJournalRowAsync`;
- startup recovery;
- lock inspection;
- `PruneEmpty`.

### Final symlink vs parent reparse distinction

Microsoft documents that deleting a symbolic-link object itself deletes the link rather than its target. That does **not** make deployment safe because a reparse point in a **parent directory** redirects the pathname used for the child.

Tests must therefore exercise parent junction/symlink components, not only a final leaf symlink.

### Topology-change windows

Three distinct times matter:

1. after plan construction but before `ApplyAsync`;
2. after whole-plan preflight;
3. after immediate per-file precondition check but before mutation.

The existing double precondition check protects **content staleness**, not **physical directory identity**.

A safe first checkpoint can reject pre-existing reparse components and add a second immediate check. A fully race-resistant design may eventually need handle/file-ID based mutation.

### Exact proposed tests

#### `Deployment_add_rejects_parent_junction_outside_game_root`

Fixture:
- external dir contains sentinel;
- create `game\nativePC\redirect` junction -> external dir;
- plan Add `nativePC\redirect\new.bin`.

Action:
- Apply.

Expected filesystem:
- no `external\new.bin`;
- external sentinel unchanged.

Expected DB:
- no Committed operation;
- no deployment_manifest row for path;
- if journal was written before detection, operation ends safely RolledBack with no pending journal mutation.

Windows-specific: yes.

#### `Deployment_replace_rejects_parent_junction_outside_game_root`

Fixture:
- external `victim.bin` contains OUTSIDE;
- expected plan references logical path under redirect.

Expected:
- external bytes unchanged;
- operation fails before mutation;
- no committed manifest transition.

#### `Deployment_remove_rejects_parent_junction_outside_game_root`

Expected:
- external victim remains present;
- no prune outside trusted root;
- DB remains before state.

#### `Rollback_does_not_follow_new_parent_junction_created_after_write`

Fixture:
- begin legitimate operation;
- after a controlled fault, replace a parent directory in the logical path with a junction to an external directory before rollback.

Expected:
- rollback does not write/delete outside root;
- operation becomes RecoveryRequired if safe in-root recovery cannot be proven.

#### `Startup_recovery_does_not_follow_parent_junction_created_while_app_was_down`

Same invariant across process restart.

#### `Prune_empty_never_traverses_or_deletes_through_reparse_component`

Fixture creates an empty path below a junction.

Expected:
- no outside directory deletion;
- stop root remains;
- junction may remain or operation fails closed according to defined contract.

### Archive comparison

`ArchiveInspector` already rejects destination/parent reparse points, so it provides a useful policy prototype.

Do not copy it mechanically into deployment without tests because deployment also needs:

- rollback/recovery consistency;
- pre-existing managed files;
- file locks;
- more severe TOCTOU concerns.

---

## 7. CAS integrity deep dive

### Existing strengths

- source bytes are SHA-256 hashed while copied to a private temp;
- XXH3 is computed in the same pass for fast scan caching;
- temp uses `CreateNew`;
- write is flushed;
- source size and last-write timestamp are compared before/after;
- a concurrent destination publisher is tolerated;
- restore uses same-directory destination staging through AtomicFileOps.

### Gap A — existing hash path is not verified

If `Root\<sha>` already exists, the verified temp is deleted.

No authoritative hash of the existing object is made first.

Test:

#### `Blob_capture_rejects_or_repairs_corrupt_preexisting_hash_path`

Fixture:
1. capture bytes A and record hash H;
2. overwrite CAS path H with corrupt bytes B while keeping filename H;
3. recapture A.

Expected:
- corrupt B is not silently accepted;
- either H is atomically repaired to A or operation fails with DataIntegrityFailure;
- DB verified timestamp must not imply B was validated.

### Gap B — restore trusts existence

#### `Blob_restore_rejects_corrupt_blob_with_matching_hash_filename`

Fixture:
- CAS path H contains bytes whose SHA != H.

Action:
- restore/deploy H.

Expected:
- destination not mutated;
- corrupt object quarantined/rejected according to future policy;
- deployment does not Commit.

### Gap C — reparse object under hash path

#### `Blob_restore_rejects_reparse_object_at_hash_path`

Fixture:
- hash pathname is a symlink to unrelated external content.

Expected:
- external target is not treated as CAS object H;
- destination unchanged;
- operation fails closed.

### Gap D — CAS root itself is a reparse point

#### `Blob_store_rejects_reparse_root_when_integrity_boundary_requires_local_storage`

This policy must first be decided. If state roots on junctions are intentionally supported, verify resolved root identity instead of blanket rejecting them.

### Gap E — hardlink alias

#### `Legacy_migration_hardlink_alias_cannot_silently_mutate_verified_CAS`

Fixture:
1. migrate a legacy blob using hardlink;
2. complete verification;
3. mutate legacy alias.

Expected contract options:
- future restore re-hashes and rejects mutated CAS; or
- migration breaks the hardlink / copies into private immutable CAS before declaring success.

Do not enforce one design in a test until the intended invariant is explicitly selected.

### Crash temp files

`.capture-*.tmp` files can remain after process death. They are ignored by hash lookup, which is safe for correctness but can leak disk space.

P2 test:
`Blob_store_startup_or_maintenance_handles_stale_capture_temp_without_touching_valid_blobs`.

---

## 8. Recursive enumeration hazards

### ModScanner

Source:

`Directory.EnumerateFiles(diskRoot, "*", SearchOption.AllDirectories)`

Microsoft/.NET states `AllDirectories` includes reparse points.

A package junction can therefore cause the scanner to:

- leave the package physical tree;
- hash arbitrary accessible external files;
- copy them into CAS;
- create logical paths based on the link-relative pathname;
- potentially recurse forever through a cycle.

Exact tests:

#### `ModScanner_skips_or_rejects_reparse_directory_outside_source_root`

Fixture:
- mod source contains normal file;
- mod source also contains junction `linked` -> external;
- external contains secret/sentinel.

Expected:
- normal file captured;
- external file not present in returned `ModFileDescriptor` list;
- external bytes never copied into CAS under their SHA due to that scan.

DB:
- no mod_files row corresponding to link-relative external file.

#### `ModScanner_does_not_loop_on_reparse_cycle`

Fixture:
- directory junction creates cycle back to ancestor.

Expected:
- scan terminates deterministically;
- cycle is rejected/skipped;
- no duplicate unbounded logical paths.

### Unmanaged adoption

Source:

`Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories)`

Exact test:

#### `UnmanagedAdoption_skips_or_rejects_reparse_directory_outside_live_root`

Expected:
- external sentinel is never copied into Imported Manual Install package;
- no `adopted_live_files` row for link-relative external path;
- external target unchanged.

### Smart Inbox direct directory import

`CopyDirectoryAsync` recursively enumerates directories and files with `AllDirectories`.

Archive inputs get ArchiveInspector protection; **directory inputs do not**.

Exact test:

#### `SmartInbox_directory_import_rejects_reparse_directory_outside_inbox_item`

Expected:
- imported destination does not contain external target bytes;
- source item is left unprocessed or imports only explicitly safe content according to future contract;
- no catalog/CAS record for external file.

#### `SmartInbox_directory_import_does_not_loop_on_reparse_cycle`

Same termination invariant.

### Implementation direction

Prefer `EnumerationOptions` with explicit reparse behavior and/or a manual directory walker that:

- does not recurse into `FileAttributes.ReparsePoint` unless explicitly allowed;
- preserves deterministic cancellation;
- enforces the physical root;
- records a diagnostic when a link is skipped/rejected.

Do not rely solely on `Path.GetRelativePath` after recursive enumeration; by then the external file has already been traversed.

---

## 9. PathRules and Windows naming edge cases

### Existing strong checks

Managed paths reject:

- rooted paths;
- UNC through the leading-root check;
- `..` and `.`;
- colon / ADS syntax;
- invalid filename characters;
- trailing dot/space;
- common DOS device names.

### Missing multi-extension reserved names

Exact tests:

`PathRules_rejects_reserved_device_with_multiple_extensions`

Inputs:
- `nativePC\NUL.tar.gz\x.bin`
- `nativePC\folder\AUX.config.json`
- `nativePC\COM1.foo.bar\x`

and archive equivalents.

The exact expected Windows namespace behavior should be confirmed in the hosted Windows test environment.

### Missing superscript device aliases

Exact tests:

`PathRules_rejects_superscript_DOS_device_aliases`

Inputs include:
- COM¹
- COM²
- COM³
- LPT¹
- LPT²
- LPT³

Use actual Unicode characters, not ASCII digits.

### Case and file identity

`PathRules.Comparer = OrdinalIgnoreCase` matches normal Windows expectations, but Windows supports per-directory case-sensitive behavior.

Two logical paths that differ only by case may therefore be:

- aliases on normal case-insensitive directories; or
- distinct files in a case-sensitive directory.

A global case-insensitive database key/comparer cannot represent both models simultaneously.

Recommended contract: managed game roots are treated as case-insensitive Windows trees unless explicit support for case-sensitive directories is added. Add a diagnostic/fail-closed check if a managed root has case-sensitive semantics rather than silently merging two physical files.

### Unicode normalization

Microsoft treats filenames largely as opaque Unicode strings; do not “fix” this by blindly NFC/NFD-normalizing path names.

The safety question is physical identity, not textual Unicode prettification.

Where alias/equivalence matters, use opened file identity / final path rather than inventing normalization.

---

## 10. Long-path audit

`src/MhwModManager.App/app.manifest` contains supported-OS and privilege settings but no `longPathAware`.

Microsoft primary documentation:

https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation

A future Windows test should generate destination paths around:

- < 240 chars
- 259/260 boundary
- 300+
- 32k extended-length where the environment supports it

and exercise:

- `PathRules`
- Blob restore/Add
- Replace
- Remove
- rollback
- archive extraction

Exact test:

`Atomic_replace_long_path_behavior_matches_application_manifest_contract`

Before changing the manifest, decide whether the product officially supports long paths. If yes, enable it as its own compatibility checkpoint and test deployment/recovery end-to-end.

---

## 11. Delete, prune, rollback, and recovery analysis

### File.Delete

A leaf symlink and a parent junction are different safety cases.

The primary containment rule should focus on the entire component chain.

### PruneEmpty

Existing protection:

- stop root converted to `Path.GetFullPath`;
- current directory converted likewise;
- string-prefix check requires current under stop;
- loop stops at stop root;
- root-preservation regression exists.

Missing:

- physical parent identity;
- reparse-component rejection;
- topology-change detection.

Therefore the normal accidental “delete game root” class is guarded, while redirected physical deletion remains insufficiently defined.

### Rollback

The byte-state logic is excellent:

- live == before -> already safe;
- live == after -> restore before;
- neither -> refuse destructive guessing.

But it assumes `dest` denotes the same physical object/location represented by the journal.

A parent junction created after the transaction can make a different external file coincidentally hash to before/after. Physical root containment must therefore be checked **before** byte-state recovery.

### Recovery

Startup recovery reuses rollback and so inherits the same physical-containment gap.

This is more important than ordinary Apply because filesystem topology can legitimately change while the application is not running.

---

## 12. Existing strong protections worth preserving

Do not let this audit obscure what already works well.

### Deployment durability

Existing tests cover:

- crash after journal;
- crash before first mutation;
- crash after file write;
- crash after files written;
- crash before final DB commit;
- crash after durable DB commit;
- crash before cleanup;
- interrupted rollback + restart;
- whole-plan stale preflight;
- external byte modification refusal;
- root-preserving empty-directory pruning.

### Archive extraction

Existing implementation has explicit reparse checks unmatched by most other filesystem services.

### Content capture

Scanner cache does not trust size/timestamp alone; it uses a fast content hash and authoritative capture.

### Logical path validation

Traversal, rooted paths, ADS colon syntax, and common device-name cases are already rejected.

### ReplaceFile same-volume staging

Replacement temp is deliberately created in the destination directory, satisfying the documented native same-volume requirement for `ReplaceFileW`.

---

## 13. Confirmed gaps and priority

### P0

1. **Live deployment physical-root escape via parent reparse point**
   - affects Add, Replace, Remove, rollback, recovery.
2. **ModScanner recursive reparse traversal**
   - can read/import outside package root and can cycle.
3. **UnmanagedAdoption recursive reparse traversal**
   - can read/copy outside live root.
4. **Smart Inbox direct-directory recursive reparse traversal**
   - can copy outside inbox item into managed library.
5. **CAS restore/capture trusts hash pathname without proving current bytes/object type**
   - corrupted/reparse hash object can be consumed as valid CAS.

### P0/P1

6. **ReplaceFileW documented partial-failure postconditions are not represented in recovery**
   - 1176 is particularly concerning with unconditional temp cleanup.

### P1

7. **Rollback/recovery physical identity not tied to journaled root/object.**
8. **Legacy migration CAS hardlink creates mutable alias unless alias policy prevents later writes.**
9. **Archive reparse check has check-to-write race.**
10. **PruneEmpty is lexical only.**
11. **DOS reserved-name check misses multi-extension and superscript aliases.**
12. **Game profile root containment is lexical and can resolve through reparse points.**

### P2

13. **Native long-path contract is undefined; app manifest lacks longPathAware.**
14. **ADS/metadata are not part of SHA-based live-state identity; contract is undocumented.**
15. **Stale CAS capture temps can accumulate after abrupt process death.**
16. **case-sensitive Windows directories are not represented by global OrdinalIgnoreCase semantics.**

---

## 14. Exact regression / fault-injection backlog

Each future checkpoint should take only one coherent boundary.

### Checkpoint A — live deployment reparse containment

Tests:
- `Deployment_add_rejects_parent_junction_outside_game_root`
- `Deployment_replace_rejects_parent_junction_outside_game_root`
- `Deployment_remove_rejects_parent_junction_outside_game_root`
- `Rollback_does_not_follow_new_parent_junction_created_after_write`
- `Startup_recovery_does_not_follow_parent_junction_created_while_app_was_down`
- `Prune_empty_never_traverses_reparse_component`

For every test assert:

Filesystem:
- outside sentinel/target unchanged;
- no outside new file;
- no unexpected directory deletion.

DB:
- operation never Committed on containment failure;
- manifest/original/mod state remain before image;
- if journal exists, state is RolledBack or RecoveryRequired according to whether in-root recovery is provable.

Windows-only: yes.

### Checkpoint B — recursive traversal

Tests:
- `ModScanner_skips_or_rejects_reparse_directory_outside_source_root`
- `ModScanner_does_not_loop_on_reparse_cycle`
- `UnmanagedAdoption_skips_or_rejects_reparse_directory_outside_live_root`
- `SmartInbox_directory_import_rejects_reparse_directory_outside_inbox_item`
- `SmartInbox_directory_import_does_not_loop_on_reparse_cycle`

Windows-only: reparse fixture yes; walker logic can also have platform-neutral unit coverage.

### Checkpoint C — CAS integrity

Tests:
- `Blob_capture_rejects_or_repairs_corrupt_preexisting_hash_path`
- `Blob_restore_rejects_corrupt_blob_with_matching_hash_filename`
- `Blob_restore_rejects_reparse_object_at_hash_path`
- `Blob_store_root_reparse_policy_is_enforced`
- `Concurrent_blob_capture_validates_winning_object_before_trust`
- `Recovery_with_corrupt_before_blob_fails_without_live_mutation`

### Checkpoint D — native replacement postconditions

Tests:
- 1175
- 1176
- 1177
- generic false-return metadata postcondition
- locked file / sharing violation
- source from different volume while staged native operands remain same-volume

Require destination/temp/backup byte assertions.

### Checkpoint E — PathRules naming

Pure unit tests first:
- multi-extension NUL/AUX/COM/LPT
- superscript COM/LPT
- archive equivalents
- mixed case.

Then minimal production correction.

### Checkpoint F — migration hardlink invariant

Test mutation through legacy alias after migration verification and define desired behavior.

### Checkpoint G — long paths

Only after product support policy is decided.

---

## 15. Performance side note

Safety checks must be measured, not removed because they are presumed expensive.

Recommended benchmark for a future containment guard:

- 1,000 / 10,000 / 50,000 deployment destinations;
- shallow and deep directory trees;
- mostly shared parent prefixes;
- zero reparse points;
- one reparse point late in tree;
- SSD-local path;
- optional network/reparse roots only if officially supported.

Measure:

- handle opens;
- directory attribute/file-ID lookups;
- wall time;
- allocation;
- cache-hit rate for already-validated parent prefixes.

A prefix cache can improve throughput, but it must not silently survive topology changes if the correctness contract depends on directory identity. File IDs / volume identity are stronger cache keys than raw path strings.

CAS restore re-hashing should also be benchmarked against realistic blob sizes. Correctness should establish the baseline first; optimization can then use verified-at metadata only if the filesystem mutation model makes that evidence trustworthy.

---

## 16. Recommended implementation order

1. **Test-only Windows reparse containment fixture** for live deployment.
2. Implement the smallest central physical-containment guard that makes Add/Replace/Remove/rollback/recovery fail closed.
3. Full hosted Windows Release Gate and exact evidence.
4. Recursive source/live walker hardening (scanner/adoption/inbox) with cycle tests.
5. Full gate.
6. CAS corrupt/reparse object tests, then integrity hardening.
7. Full gate.
8. Native `ReplaceFileW` backend seam + documented failure postcondition tests.
9. Only then change replacement/recovery semantics if tests show the expected defect.
10. PathRules device-name edge tests + fix.
11. Migration hardlink policy.
12. Long-path support decision.
13. Archive TOCTOU / handle-based containment only if the simpler reparse guard leaves a risk that the product threat model requires closing.

Do not bundle all filesystem findings into one production checkpoint.

---

## 17. Verification honesty

Actually performed in this support audit:

- inspected canonical GitHub `main` and recent relevant commits;
- confirmed PlannerSnapshotRepository closure evidence;
- read permanent continuity and active Learned Rules;
- read storage/deep SQLite audits;
- inspected canonical filesystem/core/automation/migration source;
- inspected current assertion bodies for path/deployment/hardening/multigame tests;
- inspected recursive repository tree because private code-search indexing was incomplete;
- checked Microsoft primary documentation for:
  - `ReplaceFileW`;
  - reparse points;
  - symbolic-link file-operation behavior;
  - recursive enumeration through reparse points;
  - DOS reserved names;
  - alternate data streams;
  - hardlinks;
  - long paths;
  - final-path/file-ID primitives.

Not performed:

- no local Windows runtime execution;
- no junction/symlink exploit was reproduced;
- no `dotnet test`;
- no BenchmarkDotNet;
- no hosted Windows Release Gate;
- no verification-cache promotion;
- no production source modification.

Accordingly:

- “P0/P1” means risk based on canonical source + documented Windows/.NET semantics;
- it does **not** mean a failing runtime regression has already been observed.

---

## 18. Parallel integration notes

### Deep SQLite audit

Preserve:

`_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`

It is the authority for write-side SQLite aggregate boundaries. This filesystem audit complements it, particularly where filesystem-before-DB windows cross deployment/adoption/migration.

### Prior broad test-gap PR

PR #4 contains a noncanonical broader test-gap/performance audit from an earlier support branch. Its filesystem observations overlap several findings here, but canonical main advanced substantially afterward and closed PlannerSnapshotRepository.

If PR #4 is later integrated:

- preserve this deeper filesystem audit;
- do not duplicate Learned Rule IDs;
- update stale statements in PR #4 that say PlannerSnapshotRepository is still open;
- treat this audit as the more specific filesystem authority.

---

## 19. Durable learned-rule candidate

A durable lesson justified by this audit is:

> Lexical containment is not physical containment on Windows. Any safety boundary that can mutate or import filesystem content must explicitly account for reparse traversal; `Path.GetFullPath`, `Path.GetRelativePath`, and string-prefix checks alone do not prove that the physical object remains under the trusted root.

Because open PR #4 already uses LR-003 on a parallel branch, this audit should use **LR-004** if the rule is appended.

---

## 20. Successor handoff

The successor must:

1. verify current canonical `main` before editing;
2. read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and the canonical read order;
3. read this filesystem audit and both storage/SQLite audits;
4. preserve the closed PlannerSnapshotRepository evidence;
5. take only **one** filesystem production boundary at a time;
6. start with Windows reparse containment tests before broad production refactoring;
7. never weaken byte-state rollback/recovery to work around a containment test;
8. keep filesystem and DB aggregate guarantees aligned;
9. bind any green verification claim to the exact checked source SHA;
10. update durable handoff state and explicitly require its successor to preserve and recursively pass the continuity constitution to the agent after them.

**Do not break the chain.**
