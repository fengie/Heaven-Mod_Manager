# Family inference research notes

The v8.6.18 model intentionally follows general concepts used by mature mod-management ecosystems rather than an HPN-specific naming table.

Key design observations:

- Nexus Mods source identity is represented separately from file category. Current Nexus API file categories are coarse (`main`, `optional`, `miscellaneous`) while file versions and dependencies have their own identities/chains. Therefore category is not treated as family truth.
- Vortex/FOMOD installers model user-selectable options explicitly and Vortex retains installer selections. When equivalent metadata exists locally, explicit metadata outranks name heuristics.
- Vortex dependency/install references use stable metadata such as logical file names, source/file identity and hashes. `logicalFileName` is therefore accepted as strong imported family evidence.
- Vortex supports per-file override/install instructions, reinforcing that file ownership/override behavior is separate from logical package identity.
- MO2 keeps mod enablement/priority and metadata separate; source metadata is useful provenance but should not force unsafe file merging.

Sources reviewed during design:
- https://github.com/Nexus-Mods/Vortex
- https://github.com/Nexus-Mods/Vortex/wiki/MODDINGWIKI-Users-General-How-to-create-mod-installers
- https://github.com/Nexus-Mods/fomod-installer
- https://api-docs.nexusmods.com/
- https://github.com/ModOrganizer2/modorganizer
