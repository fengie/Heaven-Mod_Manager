# Archive extraction / import physical-root safety audit

## Status

Documentation-only support checkpoint. No production C#, test suite, schema, workflow, or verification cache is changed by this branch.

- initial canonical production source inspected: `831da365c0c67e0239ad668f60fe3c78513dc63b`
- final canonical `main` reconciled before commit: `ef9c0dc93ca5270e9ee5ab3dea080b2fcb461585`
- intervening `c40879b` / `ef9c0dc` changes add and wire generic agent-training documentation; relevant archive/import production source is unchanged
- local runtime host: `heaven2`, Windows, .NET SDK `10.0.401`
- selected because the active `agent/recursive-source-reparse-hardening-20260927` branch already owns recursive source traversal
- that active branch was at `f51f72927e8f90c264df1ef197ba6c9bbe2704de` during this audit
- this audit owns only archive extraction/import physical-root containment and deliberately does not modify that active production boundary

## Scope

Inspected actual bodies and call sites in:

- `src/MhwModManager.Filesystem/ArchiveInspector.cs`
- `src/MhwModManager.Automation/ArchiveImportService.cs`
- `src/MhwModManager.Filesystem/CatalogService.cs`
- `src/MhwModManager.Core/PathRules.cs`
- `tests/MhwModManager.IntegrationTests/HardeningTests.cs`
- `tests/MhwModManager.Tests/PathRulesTests.cs`
- `Directory.Packages.props` (`SharpCompress 0.50.4`)
- existing filesystem, test-gap, and import-publication audits

This audit does not own catalog-invisible staging; `IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` remains the specialized authority for that boundary.
## Existing strengths confirmed

`ArchiveInspector.ExtractSafely` already has several meaningful defenses:

- rejects suspicious archive-relative paths through `PathRules.IsSafeArchiveRelativePath`
- normalizes each output with `Path.GetFullPath` and enforces lexical containment below its extraction destination
- rejects the extraction destination when that destination already exists as a reparse point
- walks created descendant parent directories and rejects descendant reparse points before each file write
- extracts with `Overwrite=false`
- caps entry count and declared expanded bytes
- leaves SharpCompress's checksum checking at its normal enabled default
- does not opt into a symbolic-link handler

The existing regression `HardeningTests.Archive_extraction_rejects_parent_traversal` passed on this branch.

The existing `PathRulesTests.Unsafe_archive_paths_are_rejected` cases also passed.

Those strengths are real. The defect below is a different physical-root case.

## Confirmed P1 — an ancestor junction above the destination root is followed

`ExtractSafely` checks `destination` itself only when that exact path already exists:

```csharp
if (Directory.Exists(destination) && IsReparsePoint(destination))
    throw new InvalidDataException(...);
Directory.CreateDirectory(destination);
```

It does not validate existing ancestors above `destination`.
After creation, `EnsureNoReparsePoint(root, parent)` starts at the extracted file's parent and stops when it reaches the extraction root. It therefore checks descendants of the extraction root, but never the caller-owned parent such as `modsRoot`.

That matters because `ArchiveImportService` chooses:

```text
destination = <modsRoot>\<archive name>
staging     = destination + ".importing"
```

If `modsRoot` is a pre-existing directory junction to an external directory, `staging` does not yet exist. The destination-root check therefore does not fire. `Directory.CreateDirectory(staging)` follows the junction and creates the staging tree outside the intended managed-library filesystem identity.

All later lexical checks still see a pathname beginning with the expected textual root, so extraction proceeds.

### Runtime reproduction

A temporary regression probe was added locally, executed, and then removed before this branch was prepared for commit.

Fixture:

1. create `<temp>\workspace`
2. create `<temp>\outside`
3. create junction `<temp>\workspace\Mods -> <temp>\outside`
4. create a normal ZIP containing `nativePC/x.txt`
5. call `ArchiveInspector.ExtractSafely(zip, "<workspace>\Mods\probe.importing")`
6. require an `InvalidDataException` and require the external target to remain untouched

Actual result on Windows:

```text
failed ArchivePhysicalRootProbeTests.ExtractSafely_rejects_ancestor_junction_above_destination_root
Expected InvalidDataException; actual=none; outsideExists=True
total: 1
failed: 1
succeeded: 0
```
The failure is a runtime-reproduced physical-containment defect, not a static-theory claim.

### Impact and threat boundary

A pre-existing reparse point in the managed library root or another ancestor can redirect archive writes outside the intended library tree.

This does **not** show that an ordinary malicious ZIP can create that ancestor junction by itself. The current code does not install a SharpCompress symbolic-link handler, and upstream SharpCompress documentation says symbolic-link entries are skipped when no handler is supplied.

The confirmed attack precondition is therefore filesystem-topology manipulation by another local actor/process or previously unsafe state. Under the repository's LR-004 physical-containment model, that is still a safety boundary worth enforcing.

## Why the current descendant check cannot fix this alone

`ArchiveInspector` receives only `archivePath` and `destination`.

Once `destination` is below a reparse ancestor, the method has no explicit trusted anchor telling it how far upward physical-containment validation must extend.

Checking only `destination` and descendants cannot prove that `destination` itself is physically under the intended managed-library root.

A correct future design should make the trusted anchor explicit at the import boundary rather than guessing it from string prefixes.
## Recommended independent production checkpoint

Do this only after the currently active recursive-source reparse checkpoint is reconciled/closed.

Tests first:

1. `Archive_extraction_rejects_junction_ancestor_above_destination_root`
   - direct `ArchiveInspector` characterization
   - outside sentinel unchanged
   - no external directory/file creation
   - fail closed before first payload write

2. `Archive_import_rejects_mods_root_junction_outside_workspace`
   - exercise `ArchiveImportService`
   - no final destination outside the trusted library root
   - no catalog publication
   - no external payload mutation

3. a normal non-reparse archive import still succeeds.

Implementation direction:

- give archive import/extraction an explicit trusted physical root/anchor
- validate the trusted root and every existing component from that anchor through the extraction destination before creation/write
- preserve the current per-entry lexical checks, descendant reparse checks, no-overwrite behavior, size/count limits, and checksum behavior
- do not weaken failure behavior merely to preserve import convenience
- treat final handle/file-identity TOCTOU closure as a separate design question if path-based checks remain insufficient
## Other archive risks reviewed but not promoted to confirmed defects here

- The broad audits already own incomplete DOS reserved-device-name handling; do not duplicate that work here.
- `IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` already owns the fact that `.importing` staging lives under the catalog-visible Mods root.
- A single large entry is only cancellation-checked before `WriteToFile`; cancellation latency during decompression remains an unmeasured robustness gap, not a reproduced safety defect in this audit.
- Entry-count and expanded-byte limits are present, but malformed/unknown-size and near-limit archive fixtures are still missing.
- The existing path-based descendant reparse check still has the already-documented check-to-write race; this audit does not reclassify that existing finding.

## Verification actually performed

Runtime:

- `dotnet --version` -> `10.0.401`
- temporary Windows junction probe: **reproduced unsafe external write**
- existing `Archive_extraction_rejects_parent_traversal`: **1/1 PASS**
- existing `Unsafe_archive_paths_are_rejected`: **4/4 PASS**

Repository/state:

- fetched all remotes and pruned stale refs
- verified canonical `origin/main = 831da365c0c67e0239ad668f60fe3c78513dc63b`
- inspected recent main history and current support branches/PR evidence
- inspected active recursive-source implementation branch diff and deliberately avoided it
- inspected actual source bodies, tests, and call sites listed above

External dependency research:

- current SharpCompress API documentation confirms checksum checking is enabled by default and symbolic links require an explicit `SymbolicLinkHandler`
- reviewed the 2026 SharpCompress directory-traversal advisory to distinguish its older `WriteToDirectory`/symlink-handler issue from this repository-specific trusted-root defect
## Not verified

- no production fix was implemented
- no full Core/Automation/Integration suite was run for this documentation-only branch
- no `Verify-Release.ps1` or `Build-Release.ps1` closure is claimed
- no verification cache was promoted
- no final handle-based TOCTOU solution was designed or proven

## Learned Rules

No new Learned Rule is added.

LR-004 already states the durable invariant exposed here: lexical containment is not physical filesystem containment. The new information is a concrete archive-import manifestation of that existing rule.

## Parallel-agent integration notes

- preserve `agent/recursive-source-reparse-hardening-20260927` as the more specialized authority for ModScanner / unmanaged-adoption / Smart Inbox recursive **source traversal**
- this audit is the specialized authority for archive extraction/import **destination-root ancestor** containment
- preserve `IMPORT_PUBLICATION_CATALOG_VISIBILITY_AUDIT.md` as the authority for staging visibility/commit-on-success
- do not combine those three boundaries into one broad filesystem refactor

## Successor handoff

The next agent must re-check canonical `main` before acting because the recursive-source branch may land first.

When this archive checkpoint is eventually implemented, start from the two Windows regressions above, make the trusted physical anchor explicit, and run the repository's normal exact Windows verification/build closure for the resulting production commit.

The successor inherits the permanent continuity constitution and active Learned Rules and must explicitly require its successor to preserve and recursively propagate them again.

**Do not break the chain.**
