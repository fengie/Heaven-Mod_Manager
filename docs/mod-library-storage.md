# Game-scoped mod storage (candidate)

This document describes the **proposed source behavior** in PR #785. It does not establish an installed or published release.

## New game-specific storage

Each registered canonical game ID has its own stable root:

```text
<manager home>/
  Mods by Game/
    monster-hunter-world/
      Installed/   # extracted and tracked source packages for new imports
      Downloads/   # retained, verified Browse Mods archive files
      Inbox/       # incoming manually dropped mod archives/folders
      Archive/     # safe duplicate cleanup destination
    nier-automata/
      Installed/
      Downloads/
      Inbox/
      Archive/
```

The **Dashboard** displays exact active-game paths and provides actions to open its Installed and Downloads directories. The game ID, not the display name, determines the folder; renaming the game label does not relocate the library. A path-containment check rejects malicious IDs. New mod installations and archive downloads must never resolve into another game's folder.

The root is intentionally a sibling of the historical top-level `Mods/`. Older installed MHW clients treated every immediate child of `Mods/` as an installable mod; creating `Mods/By Game/` inside that root would therefore create a false mod entry on rollback. A sibling root preserves old updater behavior.

## Zero-data-loss legacy compatibility

Nothing is automatically moved or deleted.

| Game | Legacy installed packages (still indexed) | Legacy inbox (still processed) |
| --- | --- | --- |
| Monster Hunter: World | `<manager home>/Mods/` | `<manager home>/Inbox/` |
| Other registered games | `<manager home>/Games/<game-id>/Mods/` | `<manager home>/Games/<game-id>/Inbox/` |

The existing per-game SQLite database, mod IDs, absolute source paths, blob references, deployment manifest, profiles and rollback records are unchanged. New imports land in `Mods by Game/<id>/Installed/`. The catalog enumerates both current and legacy package roots and does not enumerate `Downloads/` or `Inbox/` as installed mods. A processed item is archived under the *original* inbox's `Processed/` folder.

Historical archives and other user-owned data stay where they were; there is no migration or destructive cleanup. Old installed clients cannot discover new library packages until upgraded, but their older package paths remain intact. Rollback/forward updates must be tested on a real Windows installation before rollout is considered complete.

## Durable catalog downloads

The existing `State/.../CatalogDownloads/` directory remains temporary, lease-owned download scratch, with its original crash cleanup. After an archive passes transport limits, archive inspection, hashing, safe import, and installed-origin persistence, the application copies it into the owning game's `Downloads/` folder.

Saved files use a readable sanitized mod label and the full verified SHA-256 digest. Duplicate saves verify existing bytes; corrupt existing files are **not** overwritten. A mismatched copy is rejected, and only the current invocation's temporary copy may be cleaned. No source archive is moved or deleted by the new keeper. Saving is best-effort *after* the install is committed: if it fails, the UI must report that install succeeded but retention did not.

Retained downloads are **user-owned persistent archives**, not disposable cache. The manager never automatically purges them. This increases disk usage; inspect before deleting them manually. Provider API keys, signed URL parameters and expiring download links must not enter saved names or provenance.

## Acceptance / verification

1. Two game profiles have disjoint Installed, Downloads, Inbox and Archive paths.
2. An invalid game ID cannot escape the scoped folder.
3. Legacy and new mod packages both appear exactly once, without creating an entry from a download/archive folder.
4. Legacy inbox processing keeps its source/Processed location.
5. A successful direct catalog download yields a hash-verified durable archive; same digest deduplicates, mismatches reject, and cancelled or failed imports do not publish a misleading archive.
6. Empty NieR startup does not scan/hash base-game Data files (tracked separately by #784).
7. Native Windows compilation, scoped tests, full repository CI and installed multi-game smoke/rollback pass on exact source/artifact identity.

PR branch/code and a CI result are **not** installed runtime proof. As of the candidate's creation, the repository's shared provider-compliance review expiry (#786) blocks the full release gate and must be renewed by actual official-terms review, not bypassed.
