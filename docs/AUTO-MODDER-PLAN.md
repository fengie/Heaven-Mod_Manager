> **Planning authority notice:** current feature status, priority, ownership, and progress are centralized in [`_AGENT_CONTEXT/PROJECT_PLAN.md`](../_AGENT_CONTEXT/PROJECT_PLAN.md). This file is a design/reference document; do not maintain a competing live progress checklist here.

# Auto Modder / Mod Builder — Implementation Plan

Status: **IMPLEMENTATION STARTED — M0 LOCKED / M1 FOUNDATION IN PROGRESS**  
Date: 2026-09-29  
Canonical feature name: **Auto Modder**  
Alternate UI label: **Mod Builder**

This document defines the architecture and staged implementation plan for the data-driven mod authoring system inside the MHW Manual Mod Manager. M0 contracts are now implemented in Core: recipe/schema normalization, adapter capability/version negotiation, bounded reference expressions, typed patch planning, build-sandbox containment/budgets, generated-manifest writing, threat-model documentation, and a synthetic recipe fixture. M1 remains in progress; real adapter execution, catalog/discovery, publication, and the WPF workspace are not yet complete.

The goal is to let a user create supported Monster Hunter: World mods by choosing a recipe, entering meaningful values/IDs, previewing the exact generated changes, and producing a normal manager-owned mod package without manually unpacking/editing/repacking game data.

---

## 1. Product goal

Target simple workflow:

```text
Create Mod
  -> choose Auto Modder recipe
  -> enter values / IDs
  -> preview generated changes
  -> validate
  -> build
  -> optionally add/enable through the normal manager workflow
```

Example:

```text
Recipe: Edit armor parameters

Armor: Rathalos Helm α
Defense: 180
Fire resistance: 10
Slot 1: Level 4
Slot 2: Level 4
Slot 3: Level 4
Skill 1: Critical Eye
Skill 1 level: 3
```

The user should not need to know offsets, binary layout details, destination paths, archive internals, or numeric IDs when a human-readable catalog is available.

---

## 2. Non-goals

Initial Auto Modder work must **not** attempt to:

- generate arbitrary new 3D models or animation rigs;
- synthesize novel textures from prompts;
- inject arbitrary native DLL/gameplay code;
- run untrusted arbitrary shell/PowerShell/Python from a recipe;
- binary-splice unknown formats without a verified format adapter;
- silently write directly into the live game directory;
- replace the manager's existing deployment/rollback/Undo transaction model;
- claim support for a format merely because an external community tool can edit it manually.

Those can become separate future feature families.

---

## 3. Relationship to existing architecture

Auto Modder should extend the current manager rather than creating a second installer/deployment system.

Existing architectural rules remain authoritative:

1. Source packages are immutable once imported/indexed.
2. Live game changes flow through the existing staging/deployment transaction.
3. Provenance must survive planning, build, installation, conflict analysis, rollback, support bundles, and diagnostics.
4. Unknown structural/binary semantics fail closed.
5. Existing automatic compatibility/composition logic remains responsible for final-provider resolution when generated mods overlap other enabled mods.
6. Existing loadout/collection "recipes" describe a set of mods/configuration. Auto Modder recipes are a **different schema and namespace**: they describe how to build one generated mod.

Recommended naming:
- **Loadout Recipe** = portable desired mod/profile state.
- **Auto Mod Recipe** = typed transformation used to generate a mod.

---

## 4. User experience levels

### 4.1 Simple mode

Default mode.

User sees:
- searchable recipe gallery;
- human-readable entity selectors;
- normal controls for integers, decimals, enums, booleans, colors, files, and lists;
- descriptions and safe ranges;
- validation errors beside the affected field;
- "Preview changes";
- "Build mod";
- optional "Add to library" / "Enable".

Numeric IDs may be shown as secondary metadata but should not be required when a catalog maps a name to the ID.

### 4.2 Advanced mode

For experienced users.

Additional visibility:
- raw record IDs;
- source table/file;
- adapter and schema version;
- resolved output paths;
- operation list;
- before/after values;
- provenance/fingerprint;
- advanced fields hidden from Simple mode;
- export/import of recipe parameter sets.

Advanced mode still must not permit arbitrary executable code in a declarative recipe.

### 4.3 Developer mode

For recipe/adapter authors.

Provides:
- recipe validation;
- schema inspection;
- adapter capability inspection;
- fixture selection;
- dry-run build;
- before/after structured diff;
- package manifest inspection;
- recipe test runner;
- catalog lookup diagnostics;
- adapter SDK documentation.

Developer mode is a tooling surface, not a bypass around safety boundaries.

---

## 5. Architectural overview

```text
Auto Modder Workspace
        |
        v
Recipe Catalog / Recipe Loader
        |
        +----> Input Schema -> Dynamic Form Generator
        |                    -> ID / Entity Catalogs
        |
        v
Resolved Recipe Instance
        |
        v
Source Material Resolver
(vanilla/reference/source package)
        |
        v
Format Adapter Registry
        |
        v
Typed Patch Plan / Intermediate Representation
        |
        +----> Validation Engine
        +----> Dry-run / Diff Renderer
        +----> Conflict / Output-path Check
        |
        v
Build Sandbox
        |
        v
Generated Mod Package + Provenance Manifest
        |
        v
Existing Import / Library / Staging / Deployment Pipeline
```

The engine should be designed around a **typed patch plan** rather than allowing recipes to manipulate arbitrary bytes directly.

---

## 6. Core components

### 6.1 Recipe catalog

Responsibilities:
- discover built-in and installed recipe packs;
- validate schema version;
- validate declared game/game-build constraints;
- reject duplicate recipe IDs;
- expose searchable metadata;
- resolve recipe dependencies;
- report unsupported adapter requirements before the user begins editing.

Suggested stable identity:

```text
mhw.auto-mod.<publisher>.<recipe-name>
```

Example:

```text
mhw.auto-mod.builtin.armor-parameters
```

Recipe identity must be independent of the display name.

### 6.2 Dynamic form generator

Recipe input types should include at least:

- integer;
- decimal;
- string;
- boolean;
- enum;
- entity lookup;
- file;
- image;
- color;
- list/repeating group;
- structured group.

Field metadata:
- label;
- help text;
- required;
- default;
- min/max;
- regex where appropriate;
- simple/advanced visibility;
- units;
- entity catalog;
- conditional visibility;
- validation dependencies.

The renderer should produce ordinary WPF controls from a normalized input descriptor model rather than special-casing each recipe.

### 6.3 Game-data/entity catalogs

Purpose: users should select "Critical Eye" rather than memorizing an internal skill ID.

A catalog entry should support:
- stable catalog key;
- raw ID/value used by the game;
- localized display name;
- aliases/search tokens;
- optional category;
- optional metadata;
- game-build applicability;
- data-source provenance.

Initial useful catalogs:
- skills;
- items;
- armor/equipment;
- weapons;
- decorations;
- monsters;
- quests;
- shops/vendors where reliable mappings exist.

Catalog storage should be versioned independently from recipes.

Never silently reinterpret an ID across incompatible game builds.

### 6.4 Source material resolver

Some transforms need a known base file.

The resolver should choose from explicit trusted sources such as:
1. user-supplied source file/package;
2. manager-captured pristine/reference data for the current game build;
3. a declared built-in fixture used only for tests;
4. another allowed generated artifact where the recipe explicitly supports chaining.

The engine must record exactly which input bytes were used.

Do not download copyrighted game assets into the repository.

### 6.5 Format adapter registry

A format adapter converts between game bytes/files and a typed editable model.

Adapter contract should conceptually expose:

```text
CanRead
Parse
ValidateParsedModel
ApplyTypedOperations
Serialize
ValidateSerializedOutput
DescribeDiff
```

Optional capabilities:
- entity enumeration;
- schema introspection;
- semantic diff;
- record creation;
- record deletion;
- stable-key lookup;
- cross-record validation.

Initial implementation should target one high-confidence, well-understood MHW data format rather than attempting every format at once.

Potential later adapter families:
- structured parameter/table data;
- archive/container data;
- texture metadata/transcoding wrapper;
- model/material reference metadata;
- localization/string tables;
- quest/reward data.

A format is unsupported until round-trip and mutation fixtures prove it.

### 6.6 Patch-plan intermediate representation

Recipes should compile to a bounded IR, not perform mutation themselves.

Possible operations:

```text
SelectRecord
AssertField
SetField
SetBitField
ReplaceEnum
ReplaceReference
InsertRecord
DeleteRecord
CopyRecord
ReplaceAsset
WriteOutput
```

Each operation should carry:
- source selector;
- target selector;
- field path;
- old-value assertion when needed;
- new value;
- recipe step ID;
- diagnostic description.

The planner should be deterministic: same trusted source bytes + same recipe version + same inputs + same adapter version => same output bytes/manifest.

### 6.7 Validation engine

Validation layers:

**Input validation**
- required fields;
- numeric ranges;
- enum/catalog membership;
- file type/size.

**Recipe validation**
- schema;
- dependency declarations;
- adapter capabilities;
- invalid paths/selectors;
- cycles.

**Source validation**
- expected format/signature;
- expected build/fingerprint rules;
- required records present;
- old-value assertions.

**Semantic validation**
- cross-field rules;
- duplicate IDs;
- invalid references;
- impossible enum combinations;
- adapter-specific invariants.

**Output validation**
- reparse generated bytes;
- deterministic round-trip checks where supported;
- expected records/values;
- output containment;
- budget limits.

A failed validation blocks Build.

### 6.8 Build sandbox

Generated files must be produced in a manager-owned temporary/staging directory.

Rules:
- no recipe-controlled absolute output paths;
- normalize and containment-check every path;
- bounded total output size/file count;
- no writes into the live game directory during Build;
- cleanup on cancellation/failure;
- atomic publication of the completed generated package into the manager's source/library area;
- preserve diagnostic evidence on failure when safe.

### 6.9 Generated mod manifest

Every generated mod should include manager-readable provenance metadata, conceptually:

```json
{
  "format": "mhw-auto-mod-output",
  "formatVersion": 1,
  "recipeId": "mhw.auto-mod.builtin.armor-parameters",
  "recipeVersion": "1.0.0",
  "adapterIds": ["mhw.parameter-table"],
  "adapterVersions": ["1.0.0"],
  "gameBuild": "...",
  "sourceFingerprints": ["sha256:..."],
  "inputs": {
    "armor": 123,
    "defense": 180
  },
  "outputFingerprints": {
    "nativePC/...": "sha256:..."
  }
}
```

Do not place secrets or machine-specific private paths in portable manifests.

Parameter values should be recorded unless a field is explicitly marked non-portable/private.

### 6.10 Existing manager integration

"Build mod" should produce a normal package accepted by the existing library/import path.

Auto Modder must not bypass:
- indexing;
- issue analysis;
- relationship/family logic;
- conflict/provider resolution;
- staged enable/disable state;
- Preview changes;
- Apply transaction;
- rollback/Undo;
- diagnostics.

A generated mod should look like a normal mod with extra provenance.

---

## 7. Recipe schema direction

Preferred format: JSON or YAML authored externally, normalized internally to one typed model. JSON Schema (or equivalent validation) should define the public contract.

Illustrative YAML only:

```yaml
schema: mhw-auto-mod-recipe/v1
id: mhw.auto-mod.builtin.armor-parameters
version: 1.0.0
name: Armor Parameters
game: monster-hunter-world

requires:
  adapters:
    - id: mhw.parameter-table
      version: ">=1.0.0 <2.0.0"
  catalogs:
    - armor
    - skills

inputs:
  armor:
    type: entity
    catalog: armor
    label: Armor

  defense:
    type: integer
    label: Defense
    min: 0
    max: 9999

  skill:
    type: entity
    catalog: skills
    label: Skill

  skill_level:
    type: integer
    min: 0
    max: 7

steps:
  - op: select_record
    source: armor_table
    where:
      id: "${armor.id}"

  - op: set_field
    field: defense
    value: "${defense}"

  - op: set_field
    field: skill_1
    value: "${skill.id}"

  - op: set_field
    field: skill_1_level
    value: "${skill_level}"

outputs:
  - source: armor_table
    path: nativePC/...
```

The expression language must be deliberately small and non-Turing-complete in v1.

Allowed expression capabilities should initially be limited to:
- field/input references;
- literals;
- basic arithmetic;
- bounded conditional selection;
- catalog property access;
- simple formatting.

No reflection, filesystem access, network access, process launch, or arbitrary code evaluation.

---

## 8. Recipe packs and plugin boundary

The shipped manager core should own:
- recipe schema;
- recipe loader;
- dynamic form model;
- patch-plan IR;
- validation pipeline;
- build sandbox;
- provenance manifest;
- integration with library/deployment.

Extensibility should be separated:

### Declarative recipe packs

Most new supported modifications should require only:
- recipe file;
- metadata;
- tests/fixtures;
- catalog requirements;
- existing adapter capabilities.

A recipe pack should not need application recompilation.

### Format adapters

Adapters contain executable parsing/serialization logic and therefore have a higher trust level than declarative recipes.

For initial releases, adapters should be built-in or explicitly trusted/signed/allowlisted. Community executable adapters should not be auto-loaded merely because a mod archive contains one.

### Repository plugin workspace

If reusable external tooling, recipe authoring tools, catalog compilers, or adapter SDK tooling is packaged as repository plugins, new implementation belongs under the canonical `plugins/` workspace and follows `plugins/README.md`.

Do not duplicate core shipped application logic inside a plugin.

---

## 9. Reverse recipes / "Build Variant"

A later phase can make supported existing mods editable.

Workflow:

```text
Open supported mod
  -> compare supported files against trusted baseline
  -> adapters produce semantic diff
  -> match changes to a recipe when possible
  -> populate recipe inputs
  -> user edits values
  -> Build Variant
```

Example output:

```text
Rathalos Helm α
Defense: 62 -> 120
Fire resistance: 4 -> 10
Skill 1: Attack Boost 1 -> Critical Eye 3
```

Important limitation:
- reverse recognition must be semantic and format-aware;
- unknown bytes/records remain unexplained;
- never claim a recipe fully represents a mod if unmatched changes remain;
- the UI must distinguish "fully recognized", "partially recognized", and "unsupported".

---

## 10. Conflict and compatibility behavior

The Auto Modder build step should detect duplicate generated outputs inside one build.

Cross-mod conflicts remain a responsibility of the existing manager after the generated package is added to the library.

Useful pre-install preview:
- output paths;
- whether current enabled mods already provide those paths;
- whether the generated mod would create a Needs attention decision;
- known family/overlay relationships.

Auto Modder should not invent a new precedence system.

---

## 11. Security and safety boundary

### Declarative recipes

Recipes are data, not code.

They may:
- reference declared inputs;
- request allowlisted typed patch operations;
- target declared source aliases;
- emit relative output paths under the build root.

They may not:
- launch processes;
- execute scripts;
- use reflection/dynamic assembly loading;
- access arbitrary local files;
- perform network requests;
- read credentials;
- write outside the build sandbox.

### Executable adapters

Adapters must:
- be registered through an explicit trusted mechanism;
- receive only the source/output streams/models they need;
- obey size/depth/count budgets;
- fail closed on malformed input;
- expose deterministic version/capability metadata.

### Path safety

Reuse the repository's existing conservative path-containment doctrine:
- canonicalize;
- reject traversal;
- reject unexpected rooted paths;
- handle reparse/symlink boundaries safely;
- validate destinations again immediately before publication.

### Resource budgets

Add limits for:
- source size;
- parsed record count;
- nested structure depth;
- output file count;
- output byte total;
- recipe step count;
- diff size;
- catalog size loaded into UI.

---

## 12. Diagnostics and observability

Use the existing master diagnostics strategy.

Suggested event families:

```text
[AUTO-MOD-RECIPE]
[AUTO-MOD-SOURCE]
[AUTO-MOD-ADAPTER]
[AUTO-MOD-PLAN]
[AUTO-MOD-VALIDATE]
[AUTO-MOD-BUILD]
[AUTO-MOD-PUBLISH]
[AUTO-MOD-REVERSE]
```

Log:
- recipe ID/version;
- adapter ID/version;
- game build;
- source hashes;
- operation counts;
- output hashes;
- validation failures;
- timings.

Avoid logging:
- unnecessary absolute personal paths;
- secret/private values;
- whole copyrighted binary payloads.

---

## 13. Versioning and migration

Independent version domains:
- recipe schema version;
- individual recipe version;
- adapter API version;
- adapter implementation version;
- catalog schema/version;
- generated manifest format version.

Rules:
- breaking recipe-schema changes require an explicit new schema version;
- recipes declare compatible adapter version ranges;
- old generated manifests remain readable when feasible;
- migration must never silently change the meaning of stored IDs/parameters;
- user-created recipe parameter presets should record the recipe ID/version they were created against.

---

## 14. Testing strategy

### Unit tests

Recipe engine:
- schema validation;
- ID uniqueness;
- dependency resolution;
- expression evaluation;
- conditional inputs;
- cycle rejection;
- invalid path rejection.

Form model:
- correct control descriptor generation;
- default/range handling;
- catalog lookup;
- conditional field visibility.

Patch planner:
- deterministic IR;
- old-value assertions;
- operation ordering;
- duplicate target detection.

Sandbox:
- traversal attempts;
- rooted paths;
- symlink/reparse cases where applicable;
- cancellation cleanup;
- output budget enforcement.

### Adapter tests

Every adapter requires:
- parse fixtures;
- malformed/truncated fixtures;
- parse -> serialize round trip where valid;
- known single-field edits;
- multiple-field edits;
- boundary values;
- invalid references;
- deterministic output;
- reparse of generated output.

### Integration tests

End-to-end fixture flow:

```text
recipe + source fixture + inputs
  -> plan
  -> preview
  -> build
  -> manifest
  -> import into library
  -> stage
  -> deployment dry run
```

No integration fixture should require redistributing copyrighted game data.

Use synthetic/minimal legally safe fixtures or user-local verification where real game bytes are required.

### Golden tests

For stable adapters, preserve small synthetic fixtures with expected:
- structured parse result;
- diff;
- generated hash.

Golden files must be intentionally versioned and reviewable.

---

## 15. Proposed implementation milestones

### M0 — Architecture/spec lock

Deliverables:
- this plan;
- public recipe schema draft;
- adapter API draft;
- patch-plan IR draft;
- threat model;
- sample synthetic recipe.

Exit criteria:
- boundaries between app core, declarative recipes, and executable adapters are agreed;
- no implementation depends on arbitrary recipe code execution.

### M1 — Engine skeleton with synthetic adapter

Implement:
- recipe parser/validator;
- input descriptor model;
- bounded expression resolver;
- patch-plan IR;
- adapter registry;
- synthetic JSON/table adapter used only for tests;
- build sandbox;
- generated manifest.

No WPF production workflow required yet.

Exit criteria:
- deterministic end-to-end test from recipe + fixture -> generated package.

### M2 — First real MHW format adapter

Select exactly one high-confidence format with reliable documentation/fixtures.

Implement:
- parser;
- serializer;
- semantic record lookup;
- validation;
- first small set of recipes.

Candidate first recipe family should be chosen based on format confidence, not perceived popularity.

Exit criteria:
- local user-owned game data can be modified deterministically;
- generated file reparses and validates;
- original input is never mutated.

### M3 — Auto Modder WPF workspace

Implement:
- recipe gallery;
- generated forms;
- entity selectors;
- validation UI;
- Preview changes;
- Build;
- result summary.

Exit criteria:
- normal user can complete the first supported recipe without typing offsets or editing files manually.

### M4 — Manager lifecycle integration

Implement:
- generated package publication;
- provenance metadata;
- Add to library;
- stage/enable integration;
- overlap preview;
- support/diagnostic integration.

Exit criteria:
- generated mods use the existing transactional deployment path and Undo/rollback behavior.

### M5 — Catalog UX and recipe ecosystem

Implement:
- richer MHW catalogs;
- search/aliases/categories;
- parameter preset export/import;
- recipe-pack discovery/versioning;
- developer recipe validator.

Exit criteria:
- most inputs can be selected by human-readable name.

### M6 — Reverse recognition / Build Variant

Implement:
- semantic baseline diff;
- recipe matcher;
- full/partial/unsupported recognition;
- parameter reconstruction where unambiguous;
- Build Variant.

### M7 — Trusted adapter SDK / broader formats

Only after built-in adapter contracts are stable:
- external adapter packaging;
- trust/signing/allowlist model;
- SDK fixtures;
- compatibility matrix;
- version negotiation.

---

## 16. Prioritized implementation backlog

### P0 foundation

- [x] Define `AutoModRecipeV1` normalized model.
- [x] Define public JSON Schema.
- [x] Define adapter API v1.
- [x] Define patch-plan IR v1.
- [x] Define generated manifest v1.
- [x] Define recipe expression grammar and hard limits.
- [ ] Define source-material trust/fingerprint model.
- [x] Define build-sandbox containment rules.
- [ ] Add threat-model document/tests for recipe execution boundary.
- [ ] Add synthetic end-to-end fixtures.

### P1 engine

- [ ] Recipe catalog/discovery service.
- [ ] Schema/dependency validator.
- [ ] Input descriptor resolver.
- [ ] Entity catalog service.
- [ ] Patch planner.
- [ ] Adapter registry/capability negotiation.
- [ ] Validation coordinator.
- [ ] Semantic diff model.
- [ ] Build sandbox/publisher.
- [ ] Generated provenance manifest writer/reader.
- [ ] Cancellation and bounded-resource handling.
- [ ] Structured Auto Modder diagnostics.

### P1 first MHW vertical slice

- [ ] Research/select first supported MHW file format.
- [ ] Document format assumptions and unsupported cases.
- [ ] Build minimal legal/synthetic test fixtures.
- [ ] Implement parser.
- [ ] Implement serializer.
- [ ] Implement semantic lookup.
- [ ] Implement output validation/reparse.
- [ ] Add first built-in recipe.
- [ ] Add second recipe using the same adapter to prove adapter reuse.

### P2 UI

- [ ] Auto Modder navigation/workspace.
- [ ] Recipe gallery/search.
- [ ] Dynamic form renderer.
- [ ] Entity lookup/dropdown/search control.
- [ ] Inline validation.
- [ ] Advanced details drawer.
- [ ] Preview/diff view.
- [ ] Build progress/cancel.
- [ ] Build result + Add to library.
- [ ] Parameter preset save/load.

### P2 lifecycle integration

- [ ] Generated-package library metadata.
- [ ] Existing conflict/overlap preview integration.
- [ ] Existing Preview changes integration.
- [ ] Apply/Undo/rollback regression coverage.
- [ ] Support bundle / master-log integration.
- [ ] Game-build mismatch/revalidation behavior.

### P3 ecosystem

- [ ] Recipe-pack packaging contract.
- [ ] Recipe developer validator/CLI.
- [ ] Catalog compiler/import tooling.
- [ ] Recipe compatibility matrix.
- [ ] Documentation/examples.
- [ ] Reverse recognition engine.
- [ ] Build Variant workflow.
- [ ] Trusted external adapter model.

---

## 17. First vertical-slice acceptance criteria

The first shippable Auto Modder slice is complete only when all of the following are true:

1. A user can open Auto Modder and choose at least one real supported recipe.
2. The UI presents human-readable inputs for all common required values.
3. The engine can obtain an explicit trusted source file without mutating it.
4. The real MHW adapter parses that file and selects the intended record semantically.
5. Invalid input/build combinations fail before publication.
6. Preview shows the meaningful before/after values and output path.
7. Build occurs entirely inside a manager-owned sandbox.
8. Generated bytes are reparsed/validated before publication.
9. A generated manifest records recipe, adapter, game-build/source fingerprint, inputs, and output hashes.
10. The generated package can be added to the normal mod library.
11. Enabling/applying the generated package uses the existing deployment transaction.
12. Undo/rollback treats it exactly like another managed mod.
13. Same source + same recipe/adapter versions + same inputs produces identical output.
14. A malformed/truncated source fails closed.
15. Traversal/rooted-output attempts fail tests.
16. No recipe can run arbitrary local commands or read arbitrary files.
17. Tests use synthetic/legal fixtures in-repo; real game data remains user-local.
18. Shipped implementation follows normal version/README/CHANGELOG requirements when code finally lands.

---

## 18. Research required before M2

Before choosing the first real adapter, an implementation agent should research and record:

- which MHW formats are currently best documented;
- whether an existing permissively licensed parser can be reused;
- exact license obligations;
- expected Iceborne/current-game-build differences;
- stable record identity strategy;
- whether serialization is lossless for untouched fields;
- what community tools use as validation signals;
- what game files are safe and practical for user-local fixture verification.

Do not copy proprietary game assets or incompatible-licensed code into the repository.

Research evidence should be linked from the adapter's design doc.

---

## 19. Resolved architecture decisions

These should be treated as the starting decisions unless implementation evidence justifies revisiting them:

1. **Recipe-driven, not hard-coded per mod.**
2. **Recipes are declarative data, not scripts.**
3. **Typed patch-plan IR is the central mutation boundary.**
4. **Executable format adapters are a separate higher-trust boundary.**
5. **Builds happen in a sandbox and publish a normal mod package.**
6. **The existing manager owns deployment, conflict resolution, rollback, and Undo.**
7. **Human-readable catalogs sit in front of raw IDs whenever mappings are known.**
8. **Simple / Advanced / Developer modes are views over the same engine.**
9. **Reverse-recognition is later work, not required for the initial vertical slice.**
10. **One reliable MHW format first; expand only after adapter reuse is proven.**
11. **No copyrighted game bytes are committed as fixtures.**
12. **Core shipped Auto Modder behavior belongs in the application; reusable external tooling may use the canonical `plugins/` workspace.**

---

## 20. Suggested future repository shape

Exact namespaces may change after source inspection, but ownership should resemble:

```text
src/
  ...AutoMod/
    Recipes/
    Inputs/
    Catalogs/
    Planning/
    Validation/
    Adapters/
    Build/
    Provenance/

docs/
  AUTO-MODDER-PLAN.md
  AUTO-MODDER-RECIPE-SCHEMA.md        # M0
  AUTO-MODDER-ADAPTER-API.md          # M0
  AUTO-MODDER-THREAT-MODEL.md         # M0

plugins/
  ...                                 # optional authoring/catalog tooling only

tests/
  ...AutoMod...
    Fixtures/
    Recipes/
    Adapters/
    Integration/
```

Do not create these directories merely to match the diagram; add them when implementation begins and align them with the actual solution/project structure.

---

## 21. Implementation handoff rule

Future agents working on Auto Modder should:

1. read this document;
2. read `AGENTS.md`, `GLOBAL_GIT_DIRECTIVE.md`, `NEXT-AGENT-START-HERE.md`, and current continuity context;
3. inspect current `main` because this plan may outlive major architecture changes;
4. preserve current deployment/rollback/path-safety invariants;
5. implement the smallest milestone/vertical slice that can be independently verified;
6. avoid opening duplicate implementation branches if a compatible active branch/task already exists;
7. integrate validated completed work into canonical remote `main`;
8. update this document when implementation evidence changes an architectural decision.

