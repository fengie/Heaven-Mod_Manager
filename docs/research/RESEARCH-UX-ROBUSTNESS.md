# UX / robustness research notes for v8.6.25

This pass deliberately copied patterns, not whole products.

## What mature managers do well

- **Vortex** describes its core goal as automating complex mod-management work so users spend less time managing mods and more time playing. Its current UI work emphasizes a cleaner Mods page, sticky/customizable frequent actions, better status feedback, and background work that stays quiet unless it changes something user-visible. Its newer download/install work also separates phases and calls out UI stutter, race conditions, and recovery as first-class problems.
  - https://github.com/Nexus-Mods/Vortex
  - https://github.com/Nexus-Mods/Vortex/releases/
  - https://github.com/Nexus-Mods/Vortex/blob/master/docs/updater.md
  - https://github.com/Nexus-Mods/Vortex/issues/18211

- **Mod Organizer 2** makes effective file priority visible. Its conflict flags distinguish overwrite / overwritten / mixed / redundant states, and its profiles preserve enablement and priority separately. Community guidance repeatedly recommends inspecting the actual conflict details rather than treating every overwrite marker as an error.
  - https://github.com/ModOrganizer2/modorganizer/blob/master/src/modlist.cpp
  - https://github.com/ModOrganizer2/modorganizer/blob/master/src/profile.cpp

- **Fluffy Mod Manager** emphasizes separate active/available lists, mass actions, proper filtering, drag/drop, keyboard navigation, backups, and author-provided dependency/incompatibility/version hints.
  - https://github.com/fluffy-mods/ModManager

- Modding-community support threads repeatedly show that **ambiguous “higher/lower” wording and scary generic conflict indicators confuse users**. Winner/loser/effective-provider language is clearer, and normal overwrites should be inspectable without presenting them as fatal.

## Changes selected for this app

1. **Smart library views**: All, Enabled, Staged, Updates, Issues, Revalidate, Superseded.
2. **Dry-run deployment preview**: build the exact transactional plan and show add/replace/remove/restore counts without writing `nativePC`.
3. **Discard staged changes**: one click returns staged state to the last applied state without touching files.
4. **Asset Overlaps explorer**: informational MO2-style view of multi-provider assets, separating normal resolved overlaps from true blockers.
5. **Keyboard shortcuts**: Ctrl+F search, Ctrl+Enter apply, Ctrl+Z undo, F5 refresh analysis.
6. **Background-work isolation**: Nexus metadata/visual refreshes serialize through a gate and skip while foreground/transactional work is active.
7. **Atomic remote-image caching**: thumbnails download to bounded temporary files and are renamed only after a complete successful transfer.
8. **Lazy visual rescanning**: persisted gallery metadata is reused before recursively walking large source folders again.

The safety rule remains unchanged: convenience features may reduce clicks, but they must not silently deploy ambiguous mod updates, guess through structural conflicts, or mutate immutable source folders.
