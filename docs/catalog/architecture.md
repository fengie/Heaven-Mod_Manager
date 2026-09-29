# Unified Mod Catalog Architecture

## Goal

The catalog is an integrated acquisition layer for the existing mod manager. It does not replace the existing archive inspector, FOMOD flow, local catalog, deployment planner, or deployment executor.

```text
Provider adapters -> normalized catalog -> cached browse/search -> exact file selection
                  -> compliant download resolution -> catalog download manager
                  -> existing archive/FOMOD importer -> installed origin metadata
                  -> existing deployment/apply flow
```

## Boundaries

- `IModCatalogProvider` is the provider contract. UI code must not depend on provider-specific HTTP shapes.
- `CatalogMod`, `CatalogModFile`, `CatalogDependency`, `CatalogImage`, and related records are the normalized model.
- `ModCatalogService` aggregates enabled providers, uses a local cache, isolates provider failures, and performs conservative identity deduplication.
- `CatalogDownloadManager` owns HTTPS acquisition, bounded size, resumable `.part` downloads, atomic completion, filename sanitization, and SHA-256 calculation.
- `CatalogInstallService` bridges provider acquisition to the existing archive/FOMOD import pipeline and records the exact provider/mod/file origin after import.
- `catalog_install_origins` stores installed provider identity without removing support for manually installed mods.

## First milestone

The first provider is Nexus Mods. The architecture intentionally supports additional adapters later without adding provider-specific catalog pages. Cross-provider fuzzy merging and automatic dependency installation are later phases; first-milestone deduplication is deliberately limited to exact provider identity to avoid false merges.
