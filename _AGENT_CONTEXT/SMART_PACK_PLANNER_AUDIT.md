# Smart Auto-Fill / Smart Pack Planner architecture audit

Date: 2026-09-27  
Audit base: `6ada5a5c4cc83afadfba42bc6af6559540920e3d`  
Scope: choose exactly one safe first production boundary for the separately-scoped Smart Auto-Fill feature.

## Decision

The first production boundary is a **pure, provider-neutral Smart Pack planning core** in `MhwModManager.Core`.

It will:

- perform no network requests or provider authentication;
- perform no downloads;
- scrape no sites;
- modify no live game files;
- invoke no deployment mutation;
- change no SQLite schema or write transaction;
- consume normalized candidate facts supplied by later adapters;
- separate hard eligibility from preference ordering;
- produce a deterministic proposed selection/target assignment plus explanations.

This is intentionally upstream of the existing `DeploymentPlanner`. Smart Pack selection decides **which logical mods/candidates should enter a proposed pack**. Existing `DeploymentPlanner` remains authoritative for actual file-provider conflict resolution and deployment planning after archives have been locally inspected and represented as normal manager mods/files.

## Existing capabilities to reuse unchanged

### Conflict and deployment truth

- `DeploymentPlanner` already owns file-provider planning over `PlannerSnapshot`.
- `ConflictEngine`, `ConflictRuleIndex`, `RuleGraph`, explicit incompatible rules, exact winners, resource providers, family inference, texture semantics and fail-closed structural conflicts already define deployed-file conflict behavior.
- Explain Why already replays planner-backed decisions rather than reconstructing a second resolver.

**Consequence:** Smart Pack must not become a second file conflict engine. Candidate discovery/adapters should normalize known hard-conflict facts from existing analysis, and post-download pack verification must still pass through existing planner/deployment semantics.

### Planner read projection

`PlannerSnapshotRepository` is a closed, Windows-verified read-only seam. It already assembles mods, files, conflict rules, exact winners, resource providers, deployment manifest and originals while preserving historical two-connection behavior.

**Consequence:** do not reopen or duplicate this repository for Smart Pack.

### Identity / provenance / families / supersession

Existing state already contains:

- stable manager mod IDs;
- Nexus IDs/version lineage;
- `ModProvenance` and provenance confidence;
- source/archive metadata;
- `mod_families` / family membership and roles;
- `mod_supersession`;
- hashes and per-file SHA-256 identity;
- `IsSuperseded`, `SupersededByModId`, family IDs and family roles in `ModDescriptor`.

**Consequence:** later source-neutral logical identity must evolve/compose these facts. `NexusModId` must not become the universal identity, and the first core planner should accept a normalized `LogicalModId` rather than create a competing persistence identity system.

### Catalog and coverage

Existing storage already has:

- `armor_catalog`;
- `mod_file_armor(model_id, component)`;
- indexed `mod_files`;
- `PresentationReadRepository.GetOutfitCoverageAsync`;
- deployment manifest state.

**Consequence:** an MHW outfit adapter can later translate catalog/model occupancy into planner targets without hard-coding outfit semantics into the core planner.

### Revalidation / build / health evidence

Existing systems already track game build fingerprints, mod revalidation, trust/launch evidence, issue suspects and health gates.

**Consequence:** later candidate normalization should consume this evidence. The Smart Pack core should not reimplement game-build detection or health scoring.

## Capabilities to extend later

- Adapt existing installed/catalog state into normalized occupied targets and installed logical IDs.
- Adapt local archive inspection and existing conflict/planner evidence into normalized candidate compatibility and hard-conflict facts.
- Extend provider metadata ingestion with normalized author/tags/rating/recency/popularity evidence.
- Add a dependency normalization/resolution seam when reliable dependency metadata exists.
- Add source-neutral logical identity/mirror evidence by combining provenance, hashes, families and provider IDs.
- Add provider-specific discovery and popularity normalization behind separate independently verified boundaries.
- Feed a verified selected pack into the existing import/planning/deployment path; never create a second live-tree installer.

## Missing today

The repository does not yet have one provider-neutral model that represents:

- discovered-but-not-installed candidate mods from multiple sources;
- compatibility state/confidence before download;
- ordered user preference dimensions;
- normalized cross-source popularity;
- safe possible target assignments supplied by upstream analysis;
- source-neutral logical identity for mirrors;
- normalized dependency eligibility for discovered candidates;
- provider-neutral discovery/download capabilities.

Nexus metadata enrichment currently focuses on provenance/version/family/update/visuals. The schema does not provide a general author/tag/rating/download-popularity model. That is a later source-normalization seam, not a reason to redesign persistence now.

## Dangerous duplication to avoid

Do not introduce:

- another file conflict resolver parallel to `ConflictEngine`;
- another deployment planner or live-tree mutation path;
- a second SQLite transaction model around deployment;
- Nexus-specific universal mod identity;
- a Smart Pack-specific explanation system that guesses after selection;
- provider scraping/download code inside the pure planner;
- automatic archive/path retargeting without separately proven transformation semantics.

## First core model

The first core planner will consume normalized candidates with:

- stable candidate/source identity;
- normalized source-neutral logical mod identity;
- author/tags/category metadata;
- normalized popularity, rating and recency;
- compatibility state plus existing `Confidence`;
- normalized hard-requirement failures (dependency, loader, DLC, structure, explicit blocks, broken version, etc.);
- known hard-conflict logical-mod edges;
- preferred/native target plus proven-safe possible targets;
- whether the candidate consumes an exclusive target.

Planner request state will include:

- ordered lexicographic priority rules;
- high-confidence compatibility policy;
- already-installed logical IDs;
- currently occupied exclusive targets;
- optional popularity fallback.

The planner will not infer compatibility, dependencies or retarget safety. Those are evidence-producing upstream seams.

## Selection semantics

1. Reject incompatible/unknown/insufficient-confidence candidates under the selected compatibility policy.
2. Reject normalized hard-requirement failures.
3. Protect already-installed logical equivalents and occupied targets.
4. Apply hard conflicts before preferences; a preference can never override them.
5. Order remaining candidates lexicographically by explicit priority rules.
6. Use normalized popularity only as an explicit priority or configured fallback.
7. Deduplicate mirrors by normalized logical identity.
8. Assign exclusive targets deterministically. Native/preferred target is attempted first; proven-safe alternates are deterministic.
9. Use augmenting-path reassignment so an earlier selected candidate may move to another safe target to make room for an additional compatible candidate, without removing the earlier higher-priority candidate.
10. Leave capacity empty rather than manufacture an unsafe target or compatibility claim.
11. Emit selected/rejected decisions from the actual execution path.

## Determinism and complexity

Input enumeration order must not affect output.

Stable ordering is:

1. configured priority dimensions in user order;
2. optional normalized-popularity fallback;
3. logical mod ID;
4. stable candidate/source ID.

Target ordering is preferred/native target first, then remaining safe targets by stable ordinal ordering.

No full pairwise mod scan is introduced. Hard-conflict lookup is indexed from declared edges. Candidate sorting is `O(C log C)`. Target assignment uses sparse augmenting paths over each candidate's declared safe targets; worst-case matching cost depends on the candidate-target edge count rather than scanning every mod against every other mod.

## Explainability

The core planner owns its own selection explanation because those explanations describe **Smart Pack selection** (hard eligibility, user priority, mirror suppression, hard conflict, target assignment, popularity fallback).

It does **not** duplicate deployment Explain Why. Later UI should present both layers distinctly:

- why Smart Pack selected/rejected a candidate;
- why the existing DeploymentPlanner chose a file provider or blocked deployment.

Selected explanations are finalized after target matching so a moved candidate reports its real final target/retarget state.

## First boundary tests

Focused tests should cover at least:

- incompatible favorite author loses to compatible candidate;
- favorite author outranks popularity;
- higher-priority tag outranks lower-priority tag;
- popularity fallback fills remaining targets;
- exclusive target uniqueness;
- hard conflicts;
- deterministic safe alternate target assignment;
- strict rejection of unknown compatibility;
- mirror/logical duplicate suppression;
- protection of installed/occupied targets;
- unsafe candidates leave capacity empty;
- input-order independence;
- explanations match actual reason;
- hard requirements always outrank preference.

## Explicitly deferred next seams

1. installed/catalog -> Smart Pack request adapter;
2. local archive/post-download compatibility adapter using existing scanner/conflict/planner evidence;
3. dependency model/resolver;
4. source-neutral identity/mirror evidence consolidation;
5. provider discovery interfaces;
6. per-provider popularity normalization;
7. provider-specific download acquisition;
8. safe retarget transformation (only if semantics are proven);
9. WPF configuration/review UI;
10. final verified pack -> existing deployment path integration.

Do not start any of those in the same first production boundary.
