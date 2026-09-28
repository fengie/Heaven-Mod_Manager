# Implementation checkpoint — 2026-09-28

Status: **implemented and fully local-Windows verified; hosted Windows Release Gate pending**.

The tests-first implementation is on `agent/recursive-source-reparse-hardening-20260927`. Real junction regressions first failed against unchanged production behavior, proving scanner/adoption followed junctions and Smart Inbox imported a junction-containing tree. Production commit `f51f72927e8f90c264df1ef197ba6c9bbe2704de` adds shared fail-closed `SafeRecursiveTraversal` and migrates all three consumers.

After reconciling current canonical `main`, exact local Windows source `ba8b9a049b9b6e1c68cf24cbeb337b9e91d5dfd5` passed Integration **91/91**, Automation **21/21**, repository verifier **25/25**, functions **614/614**, call sites **6512 / 0 uncovered**, self-test **11/11**, strict analyzers PASS, ReadyToRun release publish PASS, release SHA-256 `7B46BF7CBC85F4818B49D478613E3FE20F5F83E98E416F60E2D9F79D03E7F686`.

Residuals remain explicit: path-check/open TOCTOU, hardlinks, and dedicated root/file-leaf/cycle fixture coverage. Do not conflate those with the proven descendant-junction regression.

---
# Recursive source reparse containment audit â€” 2026-09-27

## Status

Documentation-only autonomous support checkpoint.

- Canonical repository: `fengie/mhw-mods`
- Canonical branch inspected: `main`
- Canonical commit used for this support branch: `d001870d4cd3549841d8511392ae7885f174bca2`
- Support branch: `agent/support-recursive-source-containment-audit-20260927`
- Active main-programmer boundary at branch creation: **CAS integrity hosted-race closure**
- Production code changed by this support checkpoint: **no**
- Tests changed by this support checkpoint: **no**
- Verification cache changed: **no**
- New Learned Rule: **none** â€” LR-004 already governs physical/reparse containment and should remain the canonical rule.

This audit deliberately prepares the **next separate filesystem checkpoint after CAS**. It must not be merged as evidence that the traversal defect is fixed.

## Why this was the highest-value independent support lane

Canonical continuity explicitly says to finish CAS verification first and take recursive scanner/adoption/Smart Inbox containment as a separate later boundary. The CAS agent is actively changing `BlobStore`, `BlobIntegrityTests`, and CAS verification evidence. This audit does not touch that production boundary.

The existing Windows filesystem audit correctly identified recursive reparse traversal as a P0 class, but it did not yet turn that finding into a narrow implementation/test contract across the three concrete recursive source consumers. This document closes that planning gap without competing with the active CAS work.

## Scope

Inspected actual source bodies and existing nearby tests for:

- `src/MhwModManager.Filesystem/ModScanner.cs`
- `src/MhwModManager.Filesystem/UnmanagedAdoptionService.cs`
- `src/MhwModManager.Automation/SmartInboxService.cs`
- `src/MhwModManager.Filesystem/DeploymentExecutor.cs`
- `src/MhwModManager.Filesystem/ArchiveInspector.cs`
- `tests/MhwModManager.IntegrationTests/HardeningTests.cs`
- `tests/MhwModManager.IntegrationTests/MultiGameTests.cs`
- `tests/MhwModManager.AutomationTests/AutomationServiceTests.cs`
- `tests/MhwModManager.IntegrationTests/MhwModManager.IntegrationTests.csproj`

Also reconciled with:

- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`
- `_AGENT_CONTEXT/TEST_GAP_AND_PERFORMANCE_AUDIT.md`
- `_AGENT_CONTEXT/IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md`
- active LR-004 and LR-008
- current CAS continuity state

Out of scope:

- CAS object integrity and its current sharing-violation repair
- live `DeploymentExecutor` descendant containment (already closed and Windows verified)
- final handle-relative / file-ID-atomic TOCTOU elimination
- migration hardlinks
- duplicate-cleanup semantics
- import publication redesign beyond the interaction required to keep a traversal fix safe
- production implementation in this support branch

## External platform evidence

Microsoft's current .NET documentation for `SearchOption.AllDirectories` states that recursive search **includes reparse points such as mounted drives and symbolic links**, and warns that a link cycle can make the search loop indefinitely:

- https://learn.microsoft.com/dotnet/api/system.io.searchoption

Microsoft's `EnumerationOptions.AttributesToSkip` documentation says the default skipped attributes are only `Hidden | System`, not `ReparsePoint`:

- https://learn.microsoft.com/dotnet/api/system.io.enumerationoptions.attributestoskip

These external facts are used only to interpret the explicit recursive enumeration calls below. The repository findings themselves come from source inspection.

---

## Executive findings

### P0 â€” ModScanner can ingest external bytes through a package junction/symlink

Confirmed source behavior:

`ModScanner.EnumerateCandidates` uses:

```csharp
Directory.EnumerateFiles(diskRoot, "*", SearchOption.AllDirectories)
```

for every resolved package root.

For every yielded file it then:

1. derives a logical key from `Path.GetRelativePath(diskRoot, file)`;
2. optionally hashes the file for cache validation;
3. captures the file into CAS through `BlobStore.CaptureWithHashAsync`;
4. later persists the resulting descriptors with `ReplaceModFilesAsync`.

There is no root, directory, or leaf-file `FileAttributes.ReparsePoint` rejection in `ModScanner`.

A descendant junction under the package can therefore make external bytes look lexically like package-relative content and then be captured into CAS and persisted as a normal mod file.

This is stronger than a read-only information leak: the external bytes can become durable manager state.

### P0 â€” unmanaged adoption can copy and persist external bytes reached through the live tree

Confirmed source behavior:

`UnmanagedAdoptionService.FindCandidatesAsync` materializes:

```csharp
Directory.EnumerateFiles(live, "*", SearchOption.AllDirectories)
    .Select(f => (File: f, Key: ManagedKey(f)))
    .Where(...)
    .ToArray()
```

It later hashes each candidate. `AdoptAsync` copies every candidate into a new tracked source package and records adoption state in SQLite.

There is no reparse check for:

- the live root;
- descendant directories;
- leaf files.

A junction below the configured live mod root can therefore pull external files into a managed package and into durable `adopted_live_files` state.

### P0/P1 â€” Smart Inbox direct-directory import can copy external trees into Mods

Confirmed source behavior:

`SmartInboxService.ProcessAsync` treats any top-level inbox entry for which `Directory.Exists(entry)` is true as a direct-directory import.

`CopyDirectoryAsync` then performs two recursive traversals:

```csharp
Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)
Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
```

and creates/copies the corresponding tree under the final destination in `modsRoot`.

There is no reparse check on:

- the inbox item itself;
- descendant directories;
- leaf files.

A top-level inbox junction or a descendant junction can therefore cause Smart Inbox to copy bytes that are physically outside the inbox item into the managed library.

After copy, category classification performs another `SearchOption.AllDirectories` traversal over the destination. That second traversal is not the primary escape mechanism, but it should also move to the same safe traversal contract so the safety invariant is not duplicated inconsistently.

### P1 â€” recursive link cycles can bypass cancellation and hang a workflow

The Microsoft-documented cycle behavior is relevant to the current control flow.

- `ModScanner` checks cancellation only **after an enumerated file is yielded**.
- `UnmanagedAdoptionService` performs the entire recursive enumeration inside `ToArray()`; the cancellation token passed to `Task.Run` does not make the already-running synchronous enumeration cancellation-aware.
- `SmartInboxService.CopyDirectoryAsync` performs a recursive directory enumeration with no cancellation check inside the directory loop, then a recursive file enumeration.

A directory cycle that keeps recursing without yielding a useful file can therefore delay or defeat the existing cancellation checks.

This audit did not runtime-reproduce an infinite loop; the risk is source + Microsoft-documented API behavior.

### P1 â€” root reparses and leaf-file reparses need an explicit policy, not only descendant-directory checks

A first fix must define all three cases:

1. **root reparse** â€” e.g. the mod source path or inbox item itself is a junction;
2. **descendant directory reparse** â€” the common junction/symlink escape;
3. **leaf file reparse** â€” a file symlink/reparse object whose opened bytes may come from another location.

Only checking directory parents is insufficient for source ingestion because these workflows hash/copy the leaf bytes themselves.

The safest first checkpoint is to treat unsupported reparse objects as an actionable error rather than silently following them.

### P1 â€” Smart Inbox ordering makes a naÃ¯ve "throw when encountered" patch unsafe

This is the most important interaction with LR-008.

`ModScanner` first materializes the candidate list and only then begins CAS capture. A traversal preflight can therefore fail before CAS or `mod_files` mutation.

`UnmanagedAdoptionService` first discovers/hashes candidates and only then creates the destination package. A traversal preflight can fail before the managed package exists.

Smart Inbox is different:

1. it chooses the **final catalog-visible destination** under `modsRoot`;
2. `CopyDirectoryAsync` immediately creates that destination;
3. only then does recursive copy proceed;
4. recoverable exceptions are caught per item;
5. failed partial destination cleanup is not guaranteed.

Therefore merely inserting a reparse exception in the middle of `CopyDirectoryAsync` can convert the traversal vulnerability into a partial-publication residue already covered by LR-008.

The traversal checkpoint should either:

- **preflight the entire source tree for unsupported reparses before creating the destination**, or
- land together with the already-designed catalog-invisible staging/publication seam.

Do not "fix" reparse traversal by throwing after final-destination mutation has started without addressing this ordering.

---

## Existing strengths to preserve

### Live deployment has a proven fail-closed pattern

`DeploymentExecutor.EnsureNoReparseTraversal` now:

- proves lexical containment under the configured game root;
- walks existing components;
- rejects `FileAttributes.ReparsePoint`;
- is called before capture/precondition/mutation/recovery-sensitive reads;
- is covered by real Windows junction tests.

This is already closed and hosted-Windows verified. Do not reopen its production semantics for the source-traversal checkpoint.

The source-traversal implementation can reuse the **policy idea** but should not couple source traversal to `DeploymentExecutor`'s private method.

### Archive extraction already recognizes physical containment as distinct from lexical containment

`ArchiveInspector` rejects a reparse destination and walks parent directories before writing an extracted file.

That code is also private and path/write specific, but it demonstrates that the repository already accepts fail-closed reparse rejection as a valid safety policy.

### Existing Windows test fixture creation is usable

`HardeningTests.CreateDirectoryJunction` already creates a real Windows junction using:

`cmd.exe /d /c mklink /J ...`

and asserts `FileAttributes.ReparsePoint`.

The next implementation checkpoint should reuse this fixture style rather than inventing a mock-only model of junction semantics.

---

## Recommended first implementation contract

### Policy

For the first independently verifiable source-traversal checkpoint:

> Recursive package/import/adoption traversal must never follow an unsupported reparse object. The traversal root, every traversed directory, and every consumed file must be checked. Encountering a reparse object fails that operation before durable capture/copy/catalog state is committed. Ordinary non-reparse trees preserve existing behavior.

This is intentionally narrower than a complete handle-relative filesystem redesign.

### Preferred shape

Create one shared filesystem primitive in `MhwModManager.Filesystem`, conceptually similar to:

`SafeRecursiveTraversal` / `PhysicalTreeTraversal`

It should be usable by:

- `ModScanner`
- `UnmanagedAdoptionService`
- `SmartInboxService` (Automation already references Filesystem)

Prefer explicit top-down traversal over `SearchOption.AllDirectories`.

A first implementation should:

1. normalize/record the traversal root;
2. inspect the root's attributes and reject `ReparsePoint`;
3. enumerate only one directory level at a time;
4. inspect each directory entry before recursing;
5. reject descendant directory reparses;
6. inspect each file before opening/hash/copy and reject file reparses;
7. check cancellation between entries;
8. return stable lexical paths relative to the trusted root;
9. preserve current ordering where ordering matters, or make ordering explicitly deterministic.

Do **not** rely only on:

```csharp
new EnumerationOptions
{
    RecurseSubdirectories = true,
    AttributesToSkip = FileAttributes.ReparsePoint
}
```

because that silently skips reparse content instead of proving the package is safe/complete, and it does not by itself define the root-reparse policy.

### Error contract

Prefer an `IOException` or a narrowly named filesystem-safety exception with:

- the trusted root;
- the offending path;
- an explicit "reparse point" reason.

Smart Inbox already treats `IOException` as an item-level recoverable failure, which is compatible with leaving the source item untouched.

Do not expose unrelated external target paths if the error text can avoid it.

### Residual risk to document honestly

Path-attribute check then path-based open is still a TOCTOU window if another process can replace a checked component after validation.

The first checkpoint may preserve the same explicitly documented residual class as live deployment. It must not claim handle/file-ID-atomic containment unless the actual file open/copy/hash is made identity-bound.

Hardlinks are also not reparses and remain outside this checkpoint unless a separate policy is intentionally added.

---

## Required regression matrix

Add tests first in the implementation checkpoint.

### ModScanner â€” IntegrationTests / HardeningTests

#### 1. `Scanner_rejects_descendant_junction_without_capturing_external_bytes`

Windows only.

Fixture:

- ordinary package root;
- one normal in-package file;
- descendant directory junction pointing to an external directory containing a sentinel file.

Assert:

- capture fails with an actionable reparse error;
- external sentinel remains unchanged;
- no `mod_files` rows are committed for the attempted scan;
- no CAS object containing the external sentinel bytes is trusted as part of that mod;
- no partial normal-file scan result is committed.

This test intentionally requires **whole-scan fail closed**, not "skip the unsafe subtree and continue."

#### 2. `Scanner_rejects_reparse_root`

Use a junction as the mod source root itself.

Assert capture fails before normal mod-file persistence.

#### 3. `Scanner_rejects_file_reparse_leaf`

Create a file symlink/reparse leaf when the runner permits it; otherwise add a narrow test seam for the attribute classification while retaining a real junction test for directory traversal.

Assert the linked target bytes are not captured.

#### 4. `Scanner_reparse_cycle_fails_bounded_instead_of_recursing`

Create a directory junction cycle.

The fixed implementation must fail promptly with a reparse error. The test must have a bounded outer timeout so a regression cannot hang the entire release gate indefinitely.

### Unmanaged adoption â€” IntegrationTests / HardeningTests

#### 5. `Adoption_rejects_descendant_junction_before_copy_or_record`

Fixture:

- live mod root with one normal unmanaged file;
- descendant junction to an external directory with a sentinel.

Assert:

- `CountAsync` / `AdoptAsync` follows the chosen consistent fail-closed contract;
- no "Imported Manual Install ..." source folder is left behind;
- no `adoption_runs` / `adopted_live_files` success rows are committed for the failed attempt;
- external bytes are untouched.

#### 6. `Adoption_rejects_reparse_live_root`

Use a junction as the configured live root and assert no managed package or adoption record is created.

### Smart Inbox â€” AutomationTests

AutomationTests references the Automation project; IntegrationTests currently does not. Keep Smart Inbox behavior tests in AutomationTests unless project references are deliberately changed for another reason.

#### 7. `SmartInbox_direct_directory_rejects_descendant_junction_without_publishing_partial_mod`

Fixture:

- ordinary inbox directory;
- normal file that would otherwise copy first;
- descendant junction to external sentinel content.

Assert:

- one item is reported failed, not imported;
- the source remains in Inbox and is not moved to Processed;
- no catalog-visible final destination remains under Mods;
- no mod row is created for the failed item;
- external content is not copied.

This acceptance criterion intentionally combines LR-004 with LR-008 safety. If catalog-invisible staging lands first, assert staging cleanup/recovery according to that newer contract instead of requiring no temporary workspace at all.

#### 8. `SmartInbox_rejects_top_level_reparse_item`

The Inbox entry itself is a directory junction.

Assert the item is not imported or moved to Processed and no external bytes appear in Mods.

#### 9. `SmartInbox_reparse_cycle_fails_bounded`

Use a bounded test so recursive enumeration cannot hang CI.

### Preservation tests

#### 10. ordinary trees preserve behavior

Keep/extend existing happy-path tests to prove:

- MHW package mapping still produces the same logical keys;
- generic package mapping still produces the same logical keys;
- ordinary unmanaged adoption still works;
- ordinary Smart Inbox directory import still works.

The containment fix must not silently change package layout semantics.

---

## Implementation sequencing recommendation

After CAS is fully closed:

1. **Tests only first** for ModScanner + adoption + Smart Inbox.
2. Introduce one shared safe recursive traversal primitive in Filesystem.
3. Migrate **ModScanner** first because its pre-capture structure makes failure atomic at the scan-result level.
4. Migrate **UnmanagedAdoptionService** next because discovery happens before destination creation.
5. Migrate **SmartInboxService** last, explicitly reconciling with LR-008 publication semantics so a reparse failure cannot leave a catalog-visible partial destination.
6. Re-run all existing scanner/adoption/inbox happy paths.
7. Run the exact full Windows release gate before closure.

Do not combine this with CAS, migration recovery, diagnostics export, crash diagnosis, or another architectural extraction.

---

## Verification performed for this support checkpoint

Performed:

- re-established canonical `main` and observed it advance during the audit;
- reconciled the branch against new canonical head `d001870d4cd3549841d8511392ae7885f174bca2`;
- compared `797991231819d8e5693efd671e5352fd447902a0...d001870d4cd3549841d8511392ae7885f174bca2`;
- confirmed the intervening changes were CAS source/test + CAS continuity/evidence, not the three audited traversal implementations;
- inspected actual source bodies for ModScanner, unmanaged adoption, Smart Inbox, DeploymentExecutor reparse protection, and ArchiveInspector protection;
- inspected nearby actual test assertions and the existing real-junction test helper;
- checked test-project references relevant to where future Smart Inbox tests belong;
- verified the Microsoft `SearchOption.AllDirectories` reparse/cycle behavior and `EnumerationOptions.AttributesToSkip` default through current Microsoft Learn documentation;
- preserved active LR-004 and LR-008 rather than inventing a duplicate Learned Rule.

Not performed:

- no local checkout / `git status`;
- no `dotnet test`;
- no PowerShell handoff validator;
- no Windows runtime junction reproduction for the source-traversal paths;
- no hosted Windows Release Gate;
- no verification-cache promotion.

Therefore the repository behavior findings above are **source-confirmed**, while the exact runtime effects of each proposed fixture remain to be demonstrated by the future Windows tests.

---

## Parallel-work integration notes

- **CAS remains the active programmer boundary.** Do not cherry-pick production changes from this support branch because there are none.
- Live DeploymentExecutor reparse containment is already closed. Preserve it as the reference policy but do not reopen its implementation.
- LR-004 remains the authoritative reparse/physical-containment rule.
- LR-008 remains authoritative for Smart Inbox catalog-invisible publication. A future traversal fix must not create a new partial-publication path.
- The older `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md` remains the broad filesystem authority; this document is the narrower implementation/test authority for **recursive source traversal**.
- If another agent implements LR-008 first, reinterpret Smart Inbox test #7 against the newer staging contract rather than reverting publication improvements.

## Successor handoff

The next agent taking this boundary should:

1. verify current canonical `main` again;
2. confirm CAS is actually closed before starting this production checkpoint;
3. read LR-004, LR-008, this audit, the Windows filesystem audit, and the import-publication audit;
4. add the Windows regressions above before production changes;
5. implement one shared fail-closed traversal primitive rather than three unrelated ad-hoc checks;
6. preserve existing logical path/package semantics;
7. document residual TOCTOU honestly;
8. run focused tests, full local Windows verification, release build, and the exact hosted Windows Release Gate;
9. update canonical continuity only from the then-current main state;
10. explicitly require their successor to inherit, preserve, and recursively propagate the same continuity constitution to the agent after them.

## 2026-09-28 implementation stress follow-up

The implementation candidate was subsequently tested on real Windows and then adversarially re-reviewed. The follow-up found two defects not covered by the first three junction regressions:

- scanner package-root junctions could bypass the child traversal-root check after `ResolveRoots` descended into `nativePC`;
- the custom LIFO traversal reversed safe sibling order and changed Smart Inbox classification on a non-reparse fixture.

Both were reproduced before repair. The scanner root regression was the only failure in a 94-test Integration run; the classification parity regression was the only failure in a 24-test Automation run.

The minimal follow-up fix validates `mod.SourcePath` itself before root resolution and reverses the stack push order so depth-first processing preserves prior sibling ordering. New real-Windows fixtures also cover scanner/adoption/Smart Inbox root junctions and bounded junction cycles. Exact source `742484ba7a6ff07d12c0cfa1ea1a46a1b1205b4a` passes Integration **94/94**, Automation **24/24**, repository verification **25/25**, functions **615/615**, call sites **6517 / 0 uncovered**, self-test **11/11**, and ReadyToRun release publication. Release SHA-256: `665836D7BED41CD83925FD956E987E9D64D1DE618BF238157E13291F5B7731B6`.

A dedicated file-symlink/reparse-leaf fixture remains unexecuted because `heaven2` lacks symlink creation privilege. TOCTOU and hardlink policy remain separate boundaries. Hosted Windows Release Gate evidence is still pending and is required before this boundary is marked closed.

**Do not break the chain.**
