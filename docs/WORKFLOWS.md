# Loadouts, rules and diagnostics

Open **Dashboard → Loadouts, rules & diagnostics**. The window captures local packages before previewing them and pauses background metadata refresh while open.

| Workflow | Behavior |
| --- | --- |
| Collection recipes | Export `.mhwrecipe` or `.ummpack` JSON containing identity, state, family metadata, and captured hashes; no mod payloads. Read format 1 and 2. Import matches IDs or exact Nexus mod/file identity and saves a profile with unresolved entries disabled. Missing, wrong-version, hash-mismatched, superseded, and ambiguous matches remain visible. Restore matched family groups separately without replacing existing local groups. |
| Decision inspector | Browse staged effective directories/files. Select a path for enabled/disabled providers, family/priority context, hashes, exact decision code, confidence, manual/inferred rule evidence, and overlay chain. Context evidence is labeled separately from decisive evidence. |
| Rules | Create/edit/delete overlay, incompatibility, and exact-file rules. Pin/clear shared providers, create base/optional families, and stage priority relationships. Overlay cycles and invalid exact providers are rejected. Incompatible enabled packages block even without shared files. |
| Update migration | Compare captured old/replacement packages, inspect file counts and transferable rules, then upgrade. Old priority/family and transferable provider/rule/selection references move to the replacement. Missing exact files or pinned resources block migration. File state and optimistic metadata edits commit together; Undo and recovery preserve both. Old source files are retained. |
| FOMOD | Archive import detects `fomod/ModuleConfig.xml`, presents visible option groups, and installs only the resulting file plan. Choices are remembered by config SHA-256. Inbox holds these archives for interactive import. |
| Profiles | Save staged state with an optional parent; children store differences and inherit future parent changes. Flat profiles continue to load. Compare enabled/priority states, effective providers, conflict changes, and armor components using current files and global rules. |
| Crash diagnosis | Validate a passing baseline and reproducing suspect set; test subsets and complements until no single member can be removed. A combination is not individual proof of guilt. Probe cleanup runs before restoration even on cancellation. |
| Stability | Display successful launches, failure associations, rollbacks, and last observations; no quality score. |
| Relationships | Inspect a selected package's explicit rule/family neighborhood (up to 50 nodes). Overlay direction points to the winner; incompatible edges are red; cycles have red node borders. |
| Adapters | Inspect active adapter roots, executable, saves, Nexus domain, capabilities, and validation. See GAME-ADAPTER-SDK.md for extension points. |

## Deliberate boundaries

- `.ummpack` is a recipe document, not an archive of mod files. Import never downloads copyrighted payloads or automatically deploys a profile. Nexus matching uses locally captured identity, not a live Nexus search/download.
- Local compatibility rules remain authoritative during recipe import; imported families are a separate explicit action. Saved profiles still share global rules; profile inheritance covers enabled/priority state.
- FOMOD supports static/dependent plugin types, group cardinalities, flag dependencies, visible steps, required/conditional files, file/folder directives, and priorities. External game/plugin/manager version dependencies are rejected with an actionable message rather than guessed. Scripts are not executed. Equal-priority conflicting destinations are rejected. Choice IDs are reused only for identical config hashes; cross-version choices must be reviewed when installing a replacement.
- Relationship visualization shows persisted rules and family context; transient inference remains in the per-file decision inspector. The view is scoped to a package neighborhood to keep large libraries usable.
- ddmin produces a 1-minimal set under the observed outcomes, not a globally smallest set or proof against intermittent failures. The existing startup observation window remains the probe criterion.
- The WPF UI requires Windows. Linux cross-compilation and backend tests do not establish native interaction or rendering correctness.

FOMOD implementation references: https://github.com/dh-nunes/fomod-docs/blob/master/docs/tutorial.md and https://github.com/TanninOne/modorganizer-installer_fomod/blob/master/src/fomodinstallerdialog.cpp . No private repository contents were sent to research services.
