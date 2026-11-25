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

## Follow-up — explicit activation and convenient library operations (2026-11-25)

User request: selection itself must never deploy or remove a mod. This supersedes the former automatic deployment of checkbox edits. Separate three states: ephemeral batch marks, pending activation/component choices, and the last committed activation/manifest. Do not conflate selection, family identity, and actual effective file ownership.

### Evidence and design choices

| Source (primary project material, accessed 2026-11-25) | Observed pattern | Manager decision |
| --- | --- | --- |
| [Vortex FAQ](https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Users-FAQ), [Mods page](https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Users-UI-Mods-section/5a69a0274da41196e1d804c983bc4cc7f4b8b9c7) | Staging/installation distinct from deployment; batch-action toolbar. | Preserve transactional executor. Explicit Apply and selected enable/disable commands; never deploy because a selection, staged checkbox, or saved precedence changed. |
| [MO2 mod list](https://github.com/ModOrganizer2/modorganizer/blob/master/src/modlist.cpp), [profile interface](https://github.com/ModOrganizer2/modorganizer/blob/master/src/profile.h), [UI strings](https://github.com/ModOrganizer2/modorganizer/blob/master/src/organizer_en.ts) | Multiple selected mods receive one activation operation; filter state, profile separation, conflict indicators. | Applied Enabled/Disabled views, independent Pending view, selection counts including filtered-out selections; retain exact member identity across reloads. |
| [Fluffy's RimWorld ModManager](https://github.com/fluffy-mods/ModManager) | Available/active lists, mass operations, filtering, keyboard navigation, list backups. | Separate applied enabled/disabled filters rather than introducing a second competing library. Reuse existing search, profiles, Undo, keyboard shortcuts and managed-family controls. |
| [r2modman profile documentation](https://github.com/ebkr/r2modmanPlus/wiki/Profiles) | Portable named setups; imports may omit local packages or replace existing setup. | Reuse existing collection recipes, profile repositories and staged preview; do not silently discard local packages or import an unreviewed activation state. |

### Candidate implementation and acceptance

- Mark individual rows or all visible managed rows; clear marks independently, count hidden selections, exclude external/read-only packages. Do not mutate staged or applied activation during marking.
- Enable or Disable selected mods stages the requested family-aware state and applies one transactional batch. If unrelated choices were previously staged, require confirmation before the entire batch is applied. No commit on cancel; pending edits remain reviewable.
- Component checkboxes and conflict/profile choices remain staged until Apply. Launch refuses pending activation and persisted unapplied precedence rather than committing implicitly. Existing explicit Auto Populate, separate discovery/import policy, and intentional coverage actions remain distinct.
- Enabled and Disabled refer to committed activation (including partially enabled families in Enabled); Pending is independent. Review unresolved exclusive choices, dependencies, and file precedence through the current planner. Never introduce an alternate deployment implementation.
- Isolated service regressions and installed-WPF tests must demonstrate unchanged bytes/database during selection, one batch transaction, correct post-apply filters, cancel, family composition, exact byte winners, restart, Undo and cross-game isolation. Measure large selection latency and one-versus-many commits rather than claiming speedup from button counts.

### Prioritized follow-up QoL research (not shipped)

1. **Applied-versus-pending change center:** one review surface for added/removed/replaced files, old/new provider, dependency/choice blockers, exact scope, and undo/redo with durable history. Consolidate existing preview, overlaps, effective inspector and operation journal rather than duplicate them.
2. **Reusable saved views:** per-game filter/search/category combinations, pinned/favorite packages, bulk selection scope, and optional restore of view preferences. Never persist accidental activation authority or restore marks to changed family membership.
3. **Fewer steps for family resolution:** show main/component/one-of/override roles, selectable relationship evidence and exact winning texture files next to the family row. Reuse the planner's effective state and persisted human rule precedence.
4. **Import staging preference:** optional import-disabled mode distinct from background auto-discovery. Show imported/duplicate/needs-review states and let users explicitly activate selected newly imported packages. Preserve an existing intentional automatic discovery preference when adding this control; do not change it silently.
5. **Portable profile diff and recovery:** compare two named setups (including unavailable/local-only packages, exact source fingerprints and rules), preview a restoration before applying, and retain rollback. Existing profile/recipe code is the starting point.
6. **Large library interaction:** keyboard range marking, category/family grouping, selectable bulk operations scoped to exact IDs, virtualization and one batched refresh; benchmark 100/500/1000 rows with realistic family composition and active filtering. No arbitrary timing promises without measured results.

These are design findings and acceptance targets, not verified implementation claims. Candidate code requires native Windows, packaged WPF and performance evidence. Related issue #946 and PR #985 own the current work.
