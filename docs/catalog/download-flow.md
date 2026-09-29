# Catalog Download and Install Flow

```text
User selects exact files
  -> provider resolves a permitted direct or assisted flow
  -> direct: HTTPS CatalogDownloadManager
       -> bounded .part file
       -> resume when server honors Range
       -> SHA-256
       -> atomic completion
  -> assisted: open legitimate provider page
       -> user completes required provider flow
       -> choose downloaded archive
       -> size/hash validation
  -> existing ArchiveInspector
  -> existing archive or FOMOD importer
  -> register local mod
  -> persist provider/mod/file/version/source/hash origin
  -> normal staged OFF state until user applies changes
```

The catalog never executes downloaded executables automatically and does not write directly into the live game directory. Archive extraction/deployment safety remains owned by the existing importer/deployment pipeline.

Signed provider URLs are resolved for the acquisition being performed and are not stored as durable source identity. Durable identity is provider/mod/file plus source page and archive hash.
