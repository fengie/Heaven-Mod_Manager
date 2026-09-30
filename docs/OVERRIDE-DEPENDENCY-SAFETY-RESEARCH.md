# Override and dependency safety research — 2026-09-29

This note records the engineering rationale for the fail-closed conflict/dependency changes.

## Conclusions adopted

1. Treat the complete enabled dependency graph as one satisfiability problem. A mod is not deployable merely because its own files look valid; all transitive requirements must remain satisfiable in the final staged set.
2. Exact destination path is the fundamental loose-file conflict boundary. A winner means one physical provider for that exact path; the manager must not invent binary merges.
3. Ambiguous executable/plugin collisions fail closed. Human naming resemblance and overlap percentage are evidence, not proof that one DLL/EXE safely supersedes another.
4. File-vs-directory path collisions fail closed regardless of priority. A path cannot reliably be both a file and an ancestor directory.
5. A currently-live manager-owned file does not satisfy a dependency if its provider is absent from the staged set, because the same deployment is about to remove or restore that file.
6. Manual exact rules and verified same-source update/optional lineage remain authoritative. Unknown same-family subsets remain blocking until overwrite direction is proven.
7. Deployment remains transactional and rollback-safe; preflight never mutates game files.

## External references

- Gibb, Ferris, Allsopp, Gazagnaire, Madhavapeddy, **Package Managers à la Carte: A Formal Model of Dependency Resolution**, PACMPL / ICFP 2026, DOI 10.1145/3828699. The work formalizes dependency-resolution semantics and the full dependency graph.
- npm CLI documentation, **strict-peer-deps**: conflicting peer dependencies can be configured as an installation failure rather than guessed.
- Dart pub documentation, **Package dependencies**: explicitly models transitive dependencies and version constraints and warns that overrides can break applications.
- Mod Organizer 2 documentation, **Mod information window / Conflicts**: same destination files are provider conflicts and one provider wins.
- Mod Organizer 2 issue #891, **Having a file and a directory with the same name in two different mods behaves unexpectedly**: documents consumer-dependent behavior for file/directory path collisions.

## URLs

- https://doi.org/10.1145/3828699
- https://docs.npmjs.com/cli/install/
- https://dart.dev/tools/pub/dependencies
- https://github-wiki-see.page/m/ModOrganizer2/modorganizer/wiki/Mod-information-window
- https://github.com/Modorganizer2/modorganizer/issues/891
