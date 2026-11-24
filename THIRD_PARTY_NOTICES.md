# Third-Party Notices

**Last updated:** November 23, 2026

Universal Mod Manager uses third-party open-source packages. Their own licenses and notices continue to apply.

| Dependency | Version | License |
| --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| Microsoft.Data.Sqlite | 11.0.0 | MIT |
| System.IO.Hashing | 11.0.0 | MIT |
| SharpCompress | 0.50.11 | MIT |
| Serilog | 4.4.0 | Apache-2.0 |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 |
| BenchmarkDotNet | 0.15.8 | MIT |
| Microsoft.Testing.Extensions.CodeCoverage | 18.12.0 | Microsoft .NET Library license ([terms](https://www.nuget.org/packages/Microsoft.Testing.Extensions.CodeCoverage/18.12.0/License)); test-project tooling only |
| xunit.v3 | 4.0.3 | Apache-2.0 |

The package versions above are intentionally regression-checked against `Directory.Packages.props` so the notice cannot silently drift as dependencies change.

The application uses Windows system fonts through WPF (for example Segoe UI) and does not bundle a font file for those system fonts.

## Bundled font and visual-asset inventory

| Repository asset | Distribution basis |
| --- | --- |
| `src/MhwModManager.App/Assets/MHWModManager.ico` | Existing repository-controlled application icon. No third-party attribution is declared in the repository; maintainers must confirm the project has redistribution rights or replace it before a public release if provenance is uncertain. |
| Bundled font files | None. The WPF UI requests installed Windows system fonts and does not redistribute those font files. |
| Mod thumbnails / previews | Loaded from user files or configured provider metadata at runtime; they are not bundled into the application release as project artwork. |

The compliance regression checks every bundled visual/font file declared by the app project against this inventory. New bundled `.ico`, `.png`, `.jpg`, `.jpeg`, `.bmp`, `.webp`, `.ttf`, or `.otf` assets therefore require an explicit provenance/license entry in the same change.

Repository-provided artwork and icons must have project-controlled provenance or an explicit license before distribution. Do not add copied promotional art, screenshots, logos, or fonts to release assets without recording their source and permission/license here or in an adjacent provenance file.

Third-party game/mod/provider names, trademarks, and content remain the property of their respective owners. Inclusion in provider metadata or compatibility logic is not an endorsement.
