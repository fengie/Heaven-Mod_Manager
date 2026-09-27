# Automatic compatibility and composition (v8.5.0)

v8.5.0 treats the enabled logical-mod set as inputs to one deterministic composed `nativePC` / game-root tree. Source mod folders are immutable. Multiple mods may stay enabled even when one intentionally shadows a subset of another.

## Research basis

Monster Hunter: World ultimately exposes one physical file at each final path. Community MHW guidance therefore resolves same-path files by overwrite/load order rather than attempting to keep two different bytes at the same path. The Nexus MO2 guide describes this as non-destructive conflict resolution: keep both mods installed and let the intended later provider win. Multiple MHW mod pages explicitly instruct users to install a main archive and then copy optional files over it.

Examples used to shape the policy include:

- Nexus Mods, **Mod Organizer 2 for MHW Guide plus DX12 ReShade Fix** — conflict winners are controlled by load order while both source mods remain intact.
- Nexus Mods, **Orion Nova Custom Outfit** — optional files are installed into the same location and overwrite main-archive files.
- Nexus Mods, **Monster Inheritance** — documented order is requirements → main file → optional file, with replacement/overwrite.
- MHW community support discussions — a body-size optional archive replacing the main package's body files is expected; choosing overwrite yields the main mod plus the optional body component.
- MHW modding documentation for `.mrl3`, `.mod3`, `.ctc`, and `.ccl` — these are related structural/material/physics assets, so blind byte-level merging is unsafe.

## Provider precedence

The manager applies this order when evidence is strong enough:

1. explicit human incompatibility / exact-path winner / pinned resource provider / remembered overlay;
2. inferred main/base → optional component;
3. inferred patch/fix/hotfix/update layered over its parent;
4. for texture paths only, dedicated texture/skin packs over incidental copies embedded in armor packages;
5. for confidently related texture lineages, newer revision metadata over older: Nexus-style upload timestamp, date/version label, explicit Updated/Fix naming, then file revision time;
6. configured priority only when no stronger semantic relationship is proven.

Human rules always win over inference.

## Logical mod families

The library is intentionally one level above source folders. A recognized base package and its additive/overwrite components—Top, Waist, Legs, No Cape, Open Top, Optional, Patch, Fix, Update, and similar author packaging—are displayed as one logical mod. Toggling that row stages every member package together. The underlying folders remain individually indexed so provenance, hashing, rollback, and diagnostics still identify the exact source file.

Old profiles are not rewritten. If an older profile enabled only some members of a newly recognized family, the row reports PARTIAL and preserves those exact physical states until the user intentionally toggles the logical family.

`Alternative`, `Alt`, and `Variant` remain separate logical mods because they usually mean pick one. Arbitrary structural `v1`/`v2` packages also remain separate. Texture/resource revisions are the exception: related v1/v2/Updated texture packs can be represented as one logical resource family because the exact-path texture provider resolver already proves which revision should win.

## Main + optional / patch stacks

High-confidence signals include names such as `Base Name - No Cape`, `Base Name - Skimpy Waist`, `Body Size 5`, `Open Top`, `Optional`, `Patch`, `Fix`, `Hotfix`, or `Update`, combined with actual file-overlap shape. A full chain such as `Base → Open Top → Fix` resolves to one logical stack. The child wins only overlapping complete files; files unique to the main package remain in the final tree.

`Alternative`, `Alt`, or `Variant` alone is deliberately treated as ambiguous. Those labels can mean “choose one instead of the other,” so unrelated structural alternatives still appear for human choice. Same-label packages such as several `Fatalis Patch` archives are also not ordered from timestamps alone.

## Shared textures and HPN resources

Different `.tex` bytes cannot simultaneously occupy one `nativePC` path. The manager therefore keeps every source mod enabled but chooses one physical provider for each exact texture path.

For HPN/UHPN/HHPN-style `mod_hepsy` resources, a dedicated texture/skin package is preferred over stale texture copies bundled inside individual armor packages. If two dedicated texture packages are confidently the same lineage, a newer v2/update/revision supersedes v1/older content even if its numeric mod priority is lower. Explicit resource pins override this automation.

This lets dozens of armor mods continue to run together while a single current skin/normal-map provider supplies the shared resource path they all reference.

## Structural and game-data safety

Unrelated `.mod3`, `.mrl3`, `.ctc`, `.ccl`, game-data, plugin, executable, and unknown-binary replacements fail closed. The manager never decides that “newer timestamp wins” for these formats. A complete optional/patch file may replace its proven parent file, but no byte-level structural merge is attempted.

## Compacted human decisions

Only genuine direct replacement collisions reach **Needs attention** during normal operation. The UI projects raw source-package collisions onto logical mods, so dozens of path-level model/material conflicts between the same alternatives become one pairwise decision. If three or more alternatives collide in the same atomic asset scope, they are shown as one compact one-of-N choice. Selecting one stages the competing logical alternatives OFF and removes every conflict that depended on them.

The choice is deliberately mod-level rather than file-level: independent alternatives should not be silently hybridized. Auto-composed components, byte-identical resources, and shared texture providers never clutter the list. A rare collision between two members of the same logical family is surfaced at component level because pretending those two non-mergeable files can coexist would be unsafe.


## Provenance, supersession, and confidence

Nexus/local provenance is evidence, not permission to guess. Explicit Nexus Main/Optional/Update categories and version-chain relationships receive the highest automatic scores. Sidecar metadata and safe local naming/overlap evidence are weaker. Every inferred winner carries a score and evidence string and can be audited later. If independent providers cannot be ordered with trustworthy evidence, the resolver creates a human choice instead of allowing arbitrary priority to decide.

Conclusive older revisions are marked superseded rather than deleted. They remain indexed as archived members for rollback/provenance but are omitted from normal planning. Local automatic supersession is intentionally limited to texture/resource revision lineages; structural v1/v2 names alone are insufficient.

## Unmanaged live-file adoption

Files already present under the live `nativePC` but absent from the deployment manifest can be adopted non-destructively. Adoption copies them into a new immutable source package under `Mods` and records the live path + SHA-256. An unchanged adopted file is then considered tracked; a later external byte change makes it visible as unmanaged again. Adoption never deletes or rewrites the live file.

## Game build revalidation

The manager fingerprints `MonsterHunterWorld.exe`. A changed executable marks plugin/executable/game-data packages for revalidation while texture-only packages are not blanket-invalidated. This is a warning/state signal, not an automatic deletion or disable action.

## Virtual merge now; materialized merge later

The deployment plan is already a virtual merged mod: one resolved provider per final path with provenance retained. A later export feature can materialize that resolved tree as one standalone mod folder/archive by copying or hard-linking whole resolved files. It should not binary-splice `.mod3` / `.mrl3` / `.ctc` / `.ccl` until a verified format-aware merger exists.
