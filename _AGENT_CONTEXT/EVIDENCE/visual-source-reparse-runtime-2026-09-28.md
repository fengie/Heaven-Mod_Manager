# Visual source reparse runtime evidence — 2026-09-28

## Provenance

- canonical source under test: `4fd61dd33609a7c55e5aedbaad026266a410f942`
- support worktree branch: `agent/support-visual-gallery-reparse-audit-20260928`
- host: heaven2
- OS: Microsoft Windows NT 10.0.26200.0
- SDK: .NET 10.0.401
- runner: xUnit.net v3 In-Process Runner 4.0.1+8ed8aa354c, 64-bit .NET 10.0.12

## Probe design

A temporary integration-test class created real Windows directory junctions with `cmd.exe /d /c mklink /J`. It was intentionally not committed because these are characterization tests of known-bad behavior, not desired permanent assertions.

### Probe 1 — recursive gallery

- mod source: `mod1`
- external directory: `external1`
- descendant junction: `mod1\linked -> external1`
- external file: `external1\author-secret.png`, 9,000 bytes
- call: `ModVisualService.GetGalleryAsync`
- reproduced result: gallery contained `mod1\linked\author-secret.png`; reading that alias matched the external file bytes exactly.

### Probe 2 — FOMOD + metadata persistence

- descendant junction: `mod2\fomod\images -> external2`
- external file: `external2\cover.jpg`, 9,000 bytes
- FOMOD declaration: `<Image>images/cover.jpg</Image>`
- call: `NexusMetadataService.RefreshAsync(false)`
- reproduced result: `preview:m2` was persisted as the lexical junction alias `mod2\fomod\images\cover.jpg`; reading it matched the external file bytes exactly.

### Probe 3 — texture adjacent preview

- descendant junction: `mod3\nativePC\foo -> external3`
- external files: `model.tex` and `model.png`
- call: `TexturePreviewService.GetPreviewAsync(..., "nativePC\\foo\\model.tex")`
- reproduced result: service returned `mod3\nativePC\foo\model.png`; reading it matched the external PNG bytes exactly.

## Executed result

Focused direct runner:

```text
VisualReparseCharacterizationTests.Metadata_refresh_currently_persists_fomod_image_through_descendant_junction ... FINISHED
VisualReparseCharacterizationTests.Texture_preview_currently_returns_adjacent_image_through_descendant_junction ... FINISHED
VisualReparseCharacterizationTests.Recursive_gallery_currently_surfaces_image_through_descendant_junction ... FINISHED
Total: 3, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0
```

The complete integration executable was then invoked with the temporary probes present and exited 0. Expected negative FunctionVerifier fixture diagnostics were emitted by the existing verifier-behavior tests.

## Non-evidence / runner note

Two initial `dotnet test` attempts (one filtered, one unfiltered) reported zero tests with exit code 5 under the local Microsoft.Testing.Platform invocation. Those invocations are **not** counted as verification. After an explicit successful Release build, the generated xUnit v3 in-process test executable was invoked directly and is the evidence above.

## Cleanup

The temporary characterization source `tests/MhwModManager.IntegrationTests/VisualReparseCharacterizationTests.cs` was deleted before commit. No production source, permanent test source, verification cache, or promoted verification state was changed by this audit.
