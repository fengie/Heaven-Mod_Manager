# Repository Structure and Library Organization

This rule is mandatory for all repository work. The repository must evolve as an organized library, not as an accumulating flat workspace.

## Placement invariant

- Put every new file in the most specific existing folder that owns its purpose.
- Do not add a file to the repository root merely because that is convenient. Root is reserved for true repository entrypoints, toolchain-required files, and globally discoverable governance/metadata.
- When multiple files share a stable purpose, subsystem, platform, workflow, or lifecycle, group them in a dedicated subfolder. Prefer cohesive domain folders over broad dumping grounds.
- When an existing broad folder becomes crowded, split it into meaningful subfolders rather than continuing to add siblings indefinitely.
- Reuse an existing suitable folder before creating a parallel category with overlapping meaning.
- Do not create needless depth for a one-off file unless the folder represents a durable ownership boundary; organize for discoverability, not nesting for its own sake.

## Canonical homes

Use these homes unless a more specific repository convention already exists:

- `src/<domain>/<component>/` — application/library source.
- `tests/<domain>/<component>/` — tests, preferably mirroring the source ownership boundary.
- `plugins/<plugin>/` — plugin source, plugin-local tests/docs/packaging; shared plugin code follows existing plugin rules.
- `scripts/<purpose>/[<platform>/]` — automation and operator/developer scripts grouped by purpose and, when useful, platform.
- `tools/<tool>/` — standalone developer/build/verification tools.
- `docs/<topic>/` — documentation grouped by subject.
- `benchmarks/<domain>/` — benchmarks grouped by subsystem.
- `data/<domain>/` — checked-in data grouped by owner/purpose.
- `_AGENT_CONTEXT/` and `_AGENT_TRAINING/` — agent state/governance only.

Repository/toolchain-required files such as solution files, `Directory.*.props`, `global.json`, primary README/security/changelog files, and mandatory top-level agent entrypoints may remain at root when their tooling/discoverability contract requires it.

## Reorganization / move protocol

A move is not complete when the file exists at the new path. The owner must preserve the repository as a working system.

1. Inventory the files being moved and their current consumers before changing paths.
2. Search the repository for every old path/name and classify references: source/project imports, solution/project files, CI, build/package/release/update scripts, tests, docs, shortcuts/launchers, verification, and agent automation.
3. Move one coherent ownership group at a time. Avoid giant blind reshuffles that make breakage difficult to isolate.
4. Update all consumers in the same change.
5. If a root-level launcher/path is an established human-facing or compatibility contract, keep only a thin compatibility wrapper when necessary; canonical implementation belongs in the organized folder.
6. After the move, search again for stale references to the old path.
7. Run verification appropriate to everything touched: builds, tests, script smokes, CI-policy checks, packaging/release/updater checks, and user-facing launch paths where applicable.
8. Do not mark a reorganization complete until the new layout is discoverable and the old layout no longer has unexplained duplicates or stale references.

## Library-quality standard

A new contributor or agent should be able to infer where a file belongs and where to find related files from directory names alone. Related implementation, tests, tooling, and documentation should form predictable neighborhoods. If a folder name is too broad to answer "what specifically is in here?", add a meaningful subfolder boundary.

## Multi-agent coordination

Repository organization is a shared mutable boundary. Before moving files:

- discover active branches/PRs/leases and in-flight organization work;
- coordinate or continue the existing organizer's plan rather than starting a competing bulk move;
- avoid moving files currently owned by another active task unless coordinated;
- rebase/sync with current `main` before integration;
- integrate coherent batches quickly so other agents see the new canonical paths;
- after integration, tell active agents about path changes through repository context/handoff state when available.

Reviewers and integration agents must reject new root clutter, misplaced files, duplicate directory concepts, or moves that leave stale references.
