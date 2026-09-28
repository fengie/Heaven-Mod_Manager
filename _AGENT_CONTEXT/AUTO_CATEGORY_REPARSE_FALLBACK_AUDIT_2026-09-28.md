# Auto-category fallback reparse containment audit — 2026-09-28

## Canonical state and selected assignment

- Repository: `fengie/mhw-mods`
- Canonical `main` inspected immediately before work: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Support branch: `agent/support-auto-category-reparse-fallback-audit-20260928`
- Selected scope: the uncaptured-source fallback inside `AutoCategoryService.AssignMissingAsync`, specifically recursive source enumeration used only when `mod_files` has no rows.
- Deliberate exclusions: active archive streaming, updater, migration/CAS hardlink, support-bundle privacy, remote-preview egress/cache, multi-instance mutation ownership, visual/FOMOD/texture preview containment, and any broad category-policy redesign.

This was chosen because the parallel visual-containment audit explicitly identified this fallback as adjacent but excluded work. No currently open support lane found during selection owned the auto-category fallback itself.

## Executive finding

**P2 / medium source-read containment defect, statically confirmed; exact call-site runtime reproduction still pending.**

`AssignMissingAsync` falls back to:

```csharp
Directory.EnumerateFiles(mod.SourcePath, "*", SearchOption.AllDirectories)
```

when the database has no `mod_files` rows for an uncategorized mod. That recursive walk has no root/descendant reparse-point policy. The resulting relative path names are passed into `ClassifyPaths`, and a non-`Unknown` result is persisted to `mods.category`.

This exact call site was **not** separately runtime-probed by this audit. However, the parallel visual-source audit runtime-reproduced Windows descendant-junction traversal through the same `Directory.EnumerateFiles(..., SearchOption.AllDirectories)` primitive on canonical main. Active LR-004 also records the repository's established rule that recursive import/read must explicitly account for reparse traversal.

The concrete effect here is narrower than the visual audit and much narrower than prior live-tree mutation defects: filenames reached through an external junction can influence persistent automatic category metadata, and cycles/reparse topology can make the fallback enumeration unbounded or fail unpredictably. No external file bytes are intentionally opened or mutated by `AutoCategoryService` itself.

## Source evidence

### Fallback activation is narrow

`src/MhwModManager.Automation/AutoCategoryService.cs:60-80`:

- loads all mods;
- restricts work to mods whose category is blank;
- reads `mod_files.path` rows for each mod;
- enters filesystem fallback only when `paths.Count == 0` and `Directory.Exists(mod.SourcePath)`.

Therefore database-backed mods with captured file rows do **not** use this source-tree walk.

### Recursive fallback has no physical-containment guard

`AutoCategoryService.cs:79-80` performs a direct `SearchOption.AllDirectories` enumeration and converts each result to a lexical relative path.

There is no call to `SafeRecursiveTraversal`, no root reparse check, and no component-level physical-containment check before recursion.

### External names can become persistent category input

`AutoCategoryService.cs:83-90` passes the enumerated names into `ClassifyPaths`, then executes:

```sql
UPDATE mods
SET category=$c
WHERE id=$m AND (category IS NULL OR trim(category)='')
```

Thus the fallback is not a transient display-only scan. Its names can determine durable category metadata.

### Existing safe traversal is available without a new dependency cycle

`src/MhwModManager.Filesystem/SafeRecursiveTraversal.cs` already:

- rejects a reparse source root;
- enumerates top-level entries;
- rejects any reparse entry before descending;
- checks cancellation during traversal;
- preserves the repaired safe-tree traversal ordering.

`src/MhwModManager.Automation/MhwModManager.Automation.csproj` already references `MhwModManager.Filesystem`. Reusing `SafeRecursiveTraversal` from Automation therefore does not require introducing a new project dependency or a circular reference.

## Existing strengths to preserve

- Database `mod_files` rows remain authoritative when present; the source fallback only fills missing evidence.
- `ClassifyPaths` rejects unsafe/non-relative path strings through `PathRules.IsSafeArchiveRelativePath`.
- The startup fallback deliberately tolerates root-level documentation and other non-deployable source content; the existing `AutoCategoryStartupScanToleratesRootDocumentationBeforeCapture` test protects that behavior.
- Safe recursive traversal already has a repository-approved physical-containment contract and ordering repair.
- LR-004 already captures the durable engineering rule. No duplicate Learned Rule is warranted.

## Risk characterization

### Confirmed repository behavior

1. An uncategorized mod with no `mod_files` rows causes recursive enumeration of `mod.SourcePath`.
2. The fallback uses `SearchOption.AllDirectories` without reparse rejection.
3. The returned relative names are category-scored.
4. A non-`Unknown` category is persisted in SQLite.
5. The project already has a hardened recursive traversal helper usable from this project.

### Strongly supported but not directly runtime-reproduced here

Because the sibling visual audit reproduced a Windows descendant junction using the same recursive API, a descendant junction in an uncaptured mod source is expected to expose external filenames to this fallback as well.

A fixture such as `source\escape -> external` with `external\npc.bin` should cause the current fallback to see relative alias `escape\npc.bin`, which scores `Npc` and can persist that category.

### Theoretical / not claimed

- No claim is made that AutoCategory reads external file contents.
- No claim is made that it mutates external files.
- No handle-level or file-ID atomic containment guarantee is proposed.
- No exact runtime result is claimed for root junctions, junction cycles, or cancellation in this call site until focused Windows tests execute.

## Missing regression coverage

The current Automation tests include normal uncaptured-source fallback coverage but no auto-category reparse fixtures.

A focused future checkpoint should add real-Windows tests for:

1. **descendant junction:** external category-significant filenames cannot influence `mods.category`;
2. **root junction:** a reparse `mod.SourcePath` is rejected before recursive classification;
3. **bounded cycle:** a junction cycle fails/returns in bounded time rather than walking indefinitely;
4. **safe-tree parity:** the existing root-documentation + ordinary MHW source classification remains unchanged;
5. **database-row bypass:** when `mod_files` rows exist, classification still uses them and does not recursively inspect the source tree;
6. **cancellation:** cancellation during a sufficiently large fallback traversal is observed before category persistence;
7. **persistence fail-closed:** an unsafe traversal never writes a category derived from the unsafe tree.

The tests should use the repository's existing real-junction helper style rather than mocking away Windows reparse behavior.

## Recommended implementation boundary

Keep the repair narrow inside the fallback branch of `AutoCategoryService.AssignMissingAsync`.

Preferred direction:

```csharp
if (paths.Count == 0 && Directory.Exists(mod.SourcePath))
{
    var snapshot = SafeRecursiveTraversal.Snapshot(mod.SourcePath, ct);
    paths.AddRange(snapshot.Files.Select(x => Path.GetRelativePath(mod.SourcePath, x)));
}
```

Before integrating that exact shape, pin two semantics with tests:

- safe-tree path ordering/category parity must remain identical to the current fallback;
- decide explicitly whether an unsafe source aborts the whole `AssignMissingAsync` call or only skips that mod. Do not accidentally change startup failure behavior while fixing containment.

Do not bundle category scoring/tie-breaking changes into this checkpoint. The earlier recursive-source adversarial review already showed that enumeration-order changes can alter `Texture` versus `Mixed` outcomes.

## What was deliberately not changed

- no production C#;
- no permanent test source;
- no verification cache;
- no global `CURRENT_STATE`, `NEXT_STEPS`, `CURRENT_REVISION`, or handoff-manifest snapshots, because several parallel branches are actively updating canonical state;
- no Learned Rule;
- no company trainer rule.

This artifact is specialized evidence only and should be harvested without replacing newer canonical continuity snapshots.

## Verification actually performed

- Re-read canonical `main` at `4fd61dd33609a7c55e5aedbaad026266a410f942`.
- Inspected the actual `AutoCategoryService` source body, including SQL read/update boundaries.
- Inspected the current `SafeRecursiveTraversal` implementation.
- Inspected Automation and Filesystem project references to confirm helper reuse does not require a new dependency edge.
- Inspected current Automation regression coverage around `AutoCategoryStartupScanToleratesRootDocumentationBeforeCapture` and the Smart Inbox safe-tree ordering regression.
- Cross-checked active LR-004 and the parallel visual-source audit's runtime reproduction of the same recursive enumeration primitive.
- Rechecked open PR search for current auto-category ownership before selecting this lane.

## Not verified

- No Windows runtime fixture was executed for this exact AutoCategory call site.
- No `dotnet test`.
- No `Verify-Release.ps1`.
- No `Build-Release.ps1`.
- No hosted Windows Release Gate.
- No verification-cache promotion.

## Recommended independent next checkpoint

Implement the fallback-only `SafeRecursiveTraversal` substitution test-first on a fresh branch after rechecking current `main` and open PRs. Prove descendant/root/cycle rejection and ordinary uncaptured-source classification parity on Windows, then run the exact repository verification/build gates.

## Parallel-work notes / successor handoff

- Do not overlap the active visual/FOMOD/texture-preview containment implementation; this audit intentionally owns only AutoCategory's fallback.
- Preserve the active archive, updater, migration-hardlink, support-bundle, remote-preview, and multi-instance branches independently.
- If the visual branch later introduces a more general existing-path containment helper, do not widen this checkpoint just to consume it; `SafeRecursiveTraversal` already fits this recursive fallback.
- Recheck canonical `main` before implementation because the current branch inventory is moving quickly.
- The successor inherits the permanent continuity constitution and must explicitly require its own successor to preserve and recursively propagate it to the agent after them.

**Do not break the chain.**
