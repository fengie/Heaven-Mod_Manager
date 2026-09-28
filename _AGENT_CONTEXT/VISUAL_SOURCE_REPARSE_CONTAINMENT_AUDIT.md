# Visual source reparse containment audit — 2026-09-28

## Canonical state and selected assignment

- Repository: `fengie/mhw-mods`
- Canonical `origin/main` inspected before work: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- Support branch: `agent/support-visual-gallery-reparse-audit-20260928`
- Scope: read-only visual/metadata discovery under mod source roots, specifically `ModVisualService`, its metadata-refresh caller, and `TexturePreviewService`.
- Deliberate exclusions: archive extraction, scanner/adoption/Smart Inbox (already hardened), auto-category fallback traversal, updater, remote-preview HTTP trust, migration, deployment/CAS, and production implementation.

This lane was selected after checking current branches/PRs and current continuity. Archive streaming, support-bundle privacy, updater, migration-hardlink, remote-preview egress, and multi-instance work already had active owners. The older Windows filesystem audit mentioned “other less safety-critical recursive visual/metadata scans” but did not characterize or close them.

## Executive finding

**Confirmed P2 / medium — visual discovery can cross a Windows directory junction and persist/read images outside the mod source root.**

Three independent current-main paths were reproduced on Windows:

1. recursive local gallery discovery follows a descendant junction and returns an image physically outside the mod package;
2. a FOMOD `<Image>` path that is lexically under the mod root but traverses a descendant junction is accepted, and metadata refresh persists that alias into `preview:<modId>`;
3. texture preview resolution accepts a managed source path through a descendant junction and returns an adjacent external image.

No external bytes were mutated by these probes. The defect is a read-boundary/privacy and robustness violation rather than the P0 live-tree mutation/CAS escape class previously fixed elsewhere.

## Existing strengths

- FOMOD XML is parsed with `DtdProcessing.Prohibit` and `XmlResolver=null`.
- Installer image values are lexically normalized and checked against the package root.
- Local image scanning has a 1,500-file yield cap and image-size bounds.
- `SafeRecursiveTraversal` already exists and is used by the closed scanner/adoption/Smart Inbox boundary.
- Existing visual tests preserve normal local-gallery and FOMOD-image behavior.
- LR-004 already states that lexical containment is not physical containment, so no duplicate Learned Rule is needed.

## Finding A — recursive local image discovery follows reparses

`ModVisualService.FindLocalImages` currently uses:

`Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Take(1500)`

at `src/MhwModManager.Filesystem/ModVisualService.cs:105`.

On Windows, a real descendant directory junction from the mod root to an external directory caused `GetGalleryAsync` to return the junction-aliased external PNG. Reading the returned path produced the exact bytes stored outside the mod root.

The 1,500-file cap limits yielded files, but does not establish physical containment and should not be treated as a reparse/cycle policy.

## Finding B — lexical FOMOD image containment accepts a physical escape

`FindInstallerImages` builds a candidate with `Path.GetFullPath(Path.Combine(...))`, then accepts it when the candidate string starts with the lexical root and `File.Exists(candidate)` is true (`ModVisualService.cs:86-89`).

A real `fomod\images` junction pointing outside the package kept the alias string under the mod root while resolving the image externally. The path passed the current check.

This matters beyond the gallery UI: `NexusMetadataService.RefreshAsync` calls `ModVisualService.DiscoverPackageImages` for every mod (`NexusMetadataService.cs:40`) and `PersistVisualsAsync` writes accepted paths to `visuals:<modId>` and `preview:<modId>` (`NexusMetadataService.cs:348-357`). The Windows probe confirmed `preview:m2` contained the junction alias and opening that persisted path read the external bytes.

## Finding C — adjacent texture previews also cross a junction

`TexturePreviewService.ResolveSourceFile` constructs source candidates from `mod.SourcePath` and accepts them through `File.Exists` (`TexturePreviewService.cs:53-59`). `FindAdjacentImage` then returns an adjacent PNG/JPG/etc. without physical-root validation (`TexturePreviewService.cs:18-19,65+`).

With `mod\nativePC\foo` as a real junction to an external directory, a requested `nativePC\foo\model.tex` caused `GetPreviewAsync` to return the junction-aliased external `model.png`; the returned path read the exact external bytes.

## Runtime characterization

Host: heaven2 / Microsoft Windows NT 10.0.26200.0
SDK: .NET 10.0.401
Test runner: xUnit.net v3 in-process runner 4.0.1 / 64-bit .NET 10.0.12

A temporary, uncommitted integration characterization class created real directory junctions with `mklink /J` and ran three tests:

- `Recursive_gallery_currently_surfaces_image_through_descendant_junction`
- `Metadata_refresh_currently_persists_fomod_image_through_descendant_junction`
- `Texture_preview_currently_returns_adjacent_image_through_descendant_junction`

Focused direct-run result: **3 total / 3 passed / 0 failed**. Passing means each test successfully reproduced the current unsafe behavior and matched external bytes through the alias.

The full integration executable was also run with the temporary probes present and exited **0**. Its normal negative FunctionVerifier fixture diagnostics appeared on stderr as expected. The temporary characterization source was deleted before the support commit.

Two earlier `dotnet test` invocations reported zero tests under this local Microsoft.Testing.Platform invocation shape. They are recorded as runner-selection non-evidence, not as product pass/fail evidence. The direct xUnit executable is the runtime evidence used here.

See `_AGENT_CONTEXT/EVIDENCE/visual-source-reparse-runtime-2026-09-28.md`.

## Missing regression coverage

Current `VisualMetadataTests` covers normal gallery aggregation and a normal FOMOD author-image hint, but has no root/descendant reparse fixtures.

A future implementation checkpoint should add real-Windows regressions that prove:

1. recursive gallery discovery never returns an image through a root or descendant reparse;
2. FOMOD `info.xml` / `ModuleConfig.xml` and declared image paths cannot traverse a reparse component;
3. unsafe local visual aliases are not persisted into `visuals:<modId>` or `preview:<modId>`;
4. texture preview source/adjacent-image lookup cannot cross a reparse component;
5. ordinary non-reparse gallery/FOMOD behavior and ordering remain unchanged.

## Recommended implementation boundary

Keep the fix inside the visual/source-read boundary.

- Reuse the existing strict `SafeRecursiveTraversal` contract for recursive local-image discovery instead of another `SearchOption.AllDirectories` walk.
- Preserve safe-tree ordering/selection parity; earlier recursive-source hardening already demonstrated that traversal-order changes can alter behavior.
- Add a shared or narrowly scoped **existing-path physical-containment check** for exact FOMOD XML/image paths and texture-preview source/adjacent paths. It should reject a reparse root or any existing reparse component before opening/decoding/copying the file.
- Because visuals are best-effort metadata, prefer rejecting/skipping an unsafe visual rather than failing an otherwise unrelated metadata refresh.
- Do not claim handle-level/atomic identity guarantees from a path-component check; document the residual check/use TOCTOU honestly.

The implementation should be a new production verification boundary and earn a fresh exact Windows gate after integration.

## Adjacent observation deliberately not audited here

`AutoCategoryService.AssignMissingAsync` still contains a fallback `Directory.EnumerateFiles(mod.SourcePath, "*", SearchOption.AllDirectories)` when database file rows are absent. That is another residual source-read traversal, but it was not runtime-characterized in this checkpoint and should not be silently bundled into the visual fix.

## What was deliberately not changed

- no production C#;
- no permanent tests;
- no verification cache;
- no global `CURRENT_STATE` / `NEXT_STEPS` snapshots, because several parallel agents are actively changing canonical state;
- no Learned Rule (LR-004 already governs this);
- no trainer rule (the generalized physical-containment doctrine already exists).

## Successor / integration handoff

Before integrating, re-check current `origin/main` and active PRs. Preserve this audit as specialized evidence; do not overwrite newer continuity files with branch-local snapshots. If a programmer takes the fix, keep it separate from archive, remote-preview networking, updater, migration, multi-instance, and auto-category work.

The successor inherits the permanent continuity constitution and must explicitly require its own successor to preserve and recursively propagate it again to the agent after them.

**Do not break the chain.**
