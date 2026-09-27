# MHW semantic coverage and safe bulk-fill audit

Date: 2026-09-27  
Support role: independent product/architecture support agent  
Canonical base inspected: `fengie/mhw-mods` `main` at `6ada5a5c4cc83afadfba42bc6af6559540920e3d`

## Support prompt used

> Continue from canonical `fengie/mhw-mods` `main`. Read the permanent repository continuity rules and current handoff first. Inspect the current Monster Hunter: World armor catalog, semantic coverage projection, armor path parsing, mod-file armor index, asset bundles, planner/conflict behavior, scanner, categories, and outfit-family logic. Compare those capabilities with the user's intended workflow: automatically fill the remaining MHW outfit/catalog coverage with preferred authors/tags first, then popularity, while avoiding unsafe or redundant selections. Do not overlap the active SQLite, CI/supply-chain, MainWindow/WPF, test/performance, async/shutdown, or multi-source-provider support work. Do not modify production source, schema, verifier state, or the closed PlannerSnapshotRepository boundary. Produce a durable design/audit defining what a real MHW "slot" is, current coverage inaccuracies, physical-vs-display alias handling, marginal-coverage scoring, exact conflict vs semantic redundancy, candidate acceptance rules, deterministic fill behavior, test fixtures, and independently verifiable implementation checkpoints. Preserve the permanent recursive continuity constitution and pass it to the successor.

## Why this support lane was chosen

Current parallel work already covers:

- SQLite transaction atomicity;
- verification / CI / supply-chain integrity;
- MainWindow / WPF ownership;
- test gaps, failures, and performance;
- asynchronous lifetime, cancellation, and shutdown races;
- source-neutral remote discovery / download architecture.

This audit deliberately does **not** repeat the remote-provider work.

Instead it answers the MHW-specific question that the generic acquisition layer cannot answer:

> What exactly counts as an "available outfit slot," how much new coverage does a candidate add, and when is adding another armor mod useful rather than redundant or unsafe?

No production C#, XAML, schema, verification cache, workflow, or planner behavior is changed here.

---

# Executive findings

The repository already has the beginnings of semantic MHW armor coverage:

- `Armor Database.csv` maps catalog series/name rows to MHW `plNNN_NNNN` model IDs;
- `PathRules.TryGetArmorComponent` recognizes armor model IDs and five component directories;
- `mod_file_armor` persists `mod_id + path + model_id + component`;
- `PresentationReadRepository.GetOutfitCoverageAsync` projects available providers and deployed winning pieces;
- `AssetBundles` uses `armor:<modelId>:<component>` for structural atomicity;
- `DeploymentPlanner` and `ConflictEngine` provide exact-path safety and family-aware composition.

However, the current Coverage projection is a **display/reporting feature**, not yet a correct optimizer state model.

The main findings are:

1. **Catalog rows are not physical slots.**
   The current CSV contains 663 rows but only **270 distinct `model_id` values**.
   **248 distinct model IDs are referenced by more than one catalog row.**
   One physical model can therefore appear under many names/series rows.

2. **The current query groups by catalog row, not physical model.**
   A single mod targeting one physical model may appear as coverage for many catalog rows.

3. **Current "AvailableProviders" is model-wide, not component-specific.**
   It counts distinct mods touching the model, not provider count for head/chest/arms/waist/legs separately.

4. **Current "WinningPieces" is deployment-manifest state, not candidate or staged state.**
   It tells the user what currently wins in the deployed tree, but cannot directly answer how much a new remote candidate would add.

5. **The existing armor catalog does not prove that all five components exist for every model ID.**
   Therefore `uniqueModels * 5` is only a theoretical upper bound, not an authoritative coverage denominator.

6. **Exact file non-conflict and semantic non-redundancy are different.**
   Two packages may avoid exact path collisions but both target the same physical outfit component in different ways. For "fill empty outfit slots," that is redundant even when the planner considers the file sets technically composable.

The correct bulk-fill primitive should be a physical semantic key:

```
MhwArmorSlot = (modelId, component)
```

with `component` in:

```
head
chest
arms
waist
legs
```

Catalog names/series IDs should remain display aliases over those physical slots, not become independent capacity.

---

# Current implementation map

## Armor catalog import

`src/MhwModManager.Mhw/ArmorCatalogLoader.cs` imports:

```
series_id
name
model_id
```

into `armor_catalog`.

It correctly persists the source CSV hash and replaces the catalog transactionally when the data changes.

The catalog is an identity/display mapping only. It does not contain:

- component names;
- vanilla file paths;
- component availability;
- gender/body applicability;
- confidence/source metadata;
- slot aliases or canonical display selection.

Therefore it should not be treated as a complete physical-slot manifest.

## Physical armor path inference

`PathRules.TryGetArmorComponent` recognizes model IDs using:

```
pl\d{3}_\d{4}
```

and maps the immediate child directory:

- `helm` -> `head`
- `body` -> `chest`
- `arm` -> `arms`
- `wst` -> `waist`
- `leg` -> `legs`

This is already the best semantic identity available in the repository for outfit coverage.

## mod_file_armor

`ManagerDatabase.ReplaceModFilesAsync` inserts semantic armor records whenever a captured path passes `TryGetArmorComponent`.

The record grain is:

```
(mod_id, path) -> (model_id, component)
```

This means the database already contains enough information to compute:

- which physical armor slots a local mod touches;
- provider counts per slot;
- candidate marginal slot coverage after capture;
- slot redundancy between mods.

That is a major advantage: the future bulk-fill optimizer does not need a second MHW parser.

## Current Coverage query

`PresentationReadRepository.GetOutfitCoverageAsync` starts from every `armor_catalog` row and joins `mod_file_armor` only by `model_id`.

It reports:

- catalog name;
- model ID;
- count of distinct provider mods;
- distinct deployed components;
- provider display names;
- one preview.

This is useful UI data, but the grain is:

```
(series_id, name, model_id)
```

rather than:

```
(model_id, component)
```

Therefore it cannot safely be reused as the optimization state for "fill remaining slots."

---

# Catalog alias inflation

The canonical bundled CSV currently contains:

- **663 catalog rows**
- **270 unique model IDs**
- **248 model IDs with multiple catalog rows**

Examples found in the canonical data:

- `pl001_0000` appears 24 times;
- `pl003_0000` appears 6 times;
- `pl004_0000` appears 6 times;
- `pl019_0000` appears under both King Beetle and Butterfly aliases;
- many low/high-rank or alternate catalog entries share one physical model ID.

This means a naïve progress bar such as:

```
covered catalog rows / 663
```

can dramatically overstate real physical variety.

If one candidate covers `pl001_0000\body`, every display row pointing at `pl001_0000` may visually look "covered" even though the candidate only occupies one physical chest slot.

For bulk-fill, the physical model/component graph must be canonical.

Catalog aliases should be projected afterward for user-friendly names.

---

# Recommended semantic data model

## 1. Physical slot

Use a value type conceptually equivalent to:

```csharp
public sealed record MhwArmorSlot(
    string ModelId,
    MhwArmorComponent Component);
```

with:

```csharp
public enum MhwArmorComponent
{
    Head,
    Chest,
    Arms,
    Waist,
    Legs
}
```

The current string representation can remain internally if changing the public domain model is unnecessary at first.

The important point is deterministic identity.

## 2. Catalog alias

Separate display identity:

```csharp
public sealed record ArmorCatalogAlias(
    int SeriesId,
    string Name,
    string ModelId);
```

A physical model may have many aliases.

For display, choose one canonical label by explicit rule while retaining all aliases.

Do not use the first CSV row silently as permanent canonical truth unless documented.

## 3. Slot provider

Derived from `mod_file_armor`:

```csharp
public sealed record ArmorSlotProvider(
    MhwArmorSlot Slot,
    string ModId,
    IReadOnlyList<string> Paths);
```

One mod may supply many files for one slot.

## 4. Slot state

Keep several states separate:

```csharp
public sealed record ArmorSlotState(
    MhwArmorSlot Slot,
    bool ExistsInTargetUniverse,
    int LibraryProviderCount,
    string? EffectiveProviderModId,
    IReadOnlyList<string> LibraryProviderModIds);
```

Do not collapse "available in library" and "currently active" into one boolean.

---

# There are at least four different coverage metrics

The product should name these explicitly.

## 1. Catalog alias coverage

Question:

> Does any mod touch the physical model referenced by this displayed armor row?

This is useful for UI browsing only.

It is not the optimizer target.

## 2. Library physical-slot coverage

Question:

> Does the local mod library contain at least one provider for this `(modelId, component)`?

This is the primary "how much variety do I own?" metric.

## 3. Effective/deployed slot coverage

Question:

> Does the current desired/deployed configuration have an effective provider for this slot?

This is the "what am I actively using?" metric.

## 4. Target-plan coverage

Question:

> If I accepted the current bulk-fill candidate set, which currently-uncovered target slots would become covered?

This is the optimizer metric.

The UI should not mix these.

---

# Defining the target universe

The current repository does **not** have a fully authoritative complete list of valid `(modelId, component)` slots.

The CSV only proves model IDs and aliases.

Therefore the implementation should support explicit universe confidence.

## Best long-term source

The enhanced MHW adapter should eventually provide a versioned semantic slot manifest derived from known game data / vanilla asset knowledge:

```
model_id
component
exists
display aliases
source/version
```

This can be shipped as adapter data.

## Safe initial source

Before an authoritative manifest exists, define an **observed slot universe** from:

- slots seen in captured mod paths;
- slots seen in any trusted vanilla/reference manifest if available;
- manually curated known slots.

Do not claim that every one of 270 unique models necessarily has five real components.

The theoretical maximum from current path semantics is:

```
270 unique model IDs * 5 components = 1,350 possible keys
```

but this should be labeled an upper bound only.

---

# Bulk-fill objective

The user's stated goal is better modeled as:

> maximize useful new physical outfit coverage under compatibility, safety, source, count, and download budgets.

For a candidate `c`, define:

```
slots(c) = physical MHW armor slots touched by the candidate
covered = slots already covered by the local library or tentative accepted set
novel(c) = slots(c) - covered
redundant(c) = slots(c) intersect covered
```

Core metric:

```
MarginalCoverageGain(c) = |novel(c)|
```

But raw slot count alone is not enough.

A 5-piece set replacing one model may add five slots but less variety than five single-piece mods across different models, depending on user goals.

Therefore expose policy modes.

---

# Recommended fill policies

## Policy A — maximize physical slot count

Best when the user literally wants maximum covered components.

Primary value:

```
new component slots
```

## Policy B — maximize distinct outfit/model coverage

Treat the first newly-covered component on an uncovered model as especially valuable.

Example weighting:

- first component on a previously uncovered model: high bonus;
- later components completing the same model: ordinary value;
- final missing component completing a full 5-piece model: completion bonus.

This produces visual variety instead of clustering all downloads onto already-partially-covered models.

## Policy C — complete partial sets first

Prefer candidates that fill holes on already-started models.

Useful if the user wants complete outfit sets.

## Policy D — user-priority slots

Allow target filters such as:

- chest only;
- female body replacements;
- specific armor names/models;
- uncovered only;
- no head replacements;
- favorites first.

Provider/source ranking remains a separate layer.

---

# Ranking hierarchy

The semantic optimizer should not replace the source-ranking rules from the multi-source audit.

Use two dimensions:

## Eligibility/rank dimension

Examples:

1. compatible game/build;
2. preferred author;
3. preferred tags;
4. preferred source;
5. popularity;
6. recency.

## Coverage utility dimension

Examples:

1. new physical models;
2. new component slots;
3. set-completion bonus;
4. redundancy penalty.

The deterministic selector can compare candidates lexicographically:

```
hard eligibility
policy tier
coverage utility
source/popularity rank
stable identity tie-break
```

This ensures popularity does not consume capacity with a redundant mod when a slightly-less-popular candidate fills an empty slot, unless the user explicitly wants popularity to dominate coverage.

---

# Exact conflict vs semantic redundancy

These concepts must stay separate.

## Exact conflict

Two candidates provide different bytes for the same deployable path.

The existing planner/conflict engine is authoritative here.

Possible outcomes include:

- identical bytes;
- explicit winner;
- known overlay;
- same-family composition;
- safe texture selection;
- blocking structural/game-data conflict.

## Semantic redundancy

Two candidates both target:

```
(modelId, component)
```

but do not necessarily collide on every exact path.

For the normal deployment system this may be legal.

For "fill empty outfit slots," it may be undesirable because the second candidate does not increase coverage.

Therefore semantic redundancy should normally be a **selection penalty**, not a fabricated conflict.

Do not teach `ConflictEngine` that two mods are incompatible merely because they share one armor slot.

That would corrupt deployment semantics.

---

# Coverage candidate classification

After archive inspection/capture, classify every candidate:

## Pure novel

All recognized armor slots are currently uncovered.

Best bulk-fill candidate.

## Mixed novel/redundant

Adds some new slots and touches some already-covered slots.

May still be worth accepting.

## Pure redundant

Adds no new physical slot.

Normally skip in "fill gaps" mode unless favored-author/popularity policy explicitly allows alternatives.

## Non-armor mixed package

Contains armor slots plus other assets.

Requires planner safety plus policy handling for unrelated payloads.

## Unknown semantic package

Looks like armor by category/name but produces no recognized armor slots.

Do not count it as coverage.

It may still be a valid mod, but should not satisfy the MHW coverage objective.

---

# Proposed candidate utility record

A future pure calculation layer could expose:

```csharp
public sealed record ArmorCoverageCandidateEvaluation(
    string CandidateId,
    IReadOnlySet<MhwArmorSlot> Slots,
    IReadOnlySet<MhwArmorSlot> NovelSlots,
    IReadOnlySet<MhwArmorSlot> RedundantSlots,
    int NewModelCount,
    int NewSlotCount,
    int CompletedModelCount,
    int RedundantSlotCount,
    bool HasUnknownArmorLikeContent,
    bool PlannerBlocked,
    IReadOnlyList<string> PlannerBlockingReasons);
```

This should be a pure model/calculation first.

Do not make it depend on WPF.

---

# Recommended scoring shape

Avoid one unexplained magic number.

Represent a score breakdown:

```csharp
public sealed record ArmorCoverageUtility(
    int NewModelScore,
    int NewSlotScore,
    int CompletionScore,
    int RedundancyPenalty,
    int UnknownPenalty,
    int Total);
```

Example defaults only:

- +100 per newly covered model;
- +25 per newly covered component;
- +75 for completing a model;
- -20 per redundant component;
- reject rather than penalize planner-blocked candidates.

These values should be policy configuration, not buried constants.

The selection explanation can then say:

> Adds 2 new models and 4 new components; completes 1 partial set; overlaps 1 already-covered waist slot.

---

# Tentative-set evaluation matters

Candidate utility changes after every accepted candidate.

Example:

- Candidate A covers `pl100_0000 chest + arms`.
- Candidate B covers `pl100_0000 chest + waist`.

Before selection, both have useful novelty.

After A is accepted, B's chest is redundant and only waist remains novel.

Therefore the algorithm must recompute marginal utility after each acceptance.

This makes a simple greedy loop appropriate:

1. evaluate all remaining eligible candidates against current tentative coverage;
2. choose highest deterministic utility/rank;
3. exact planner simulation;
4. accept if safe;
5. update tentative coverage;
6. reevaluate remaining candidates;
7. stop at budget/target.

Do not pre-sort once and assume utility is static.

---

# Suggested deterministic greedy selector

Pseudo-flow:

```
covered = current library physical-slot coverage
accepted = []

while budget remains:
    eligible = remaining candidates passing hard gates

    for candidate in eligible:
        evaluation = Evaluate(candidate, covered, accepted)

    best = stable maximum by:
        user policy tier
        coverage utility
        popularity/source tie-breaks
        canonical remote identity

    if no candidate has acceptable utility:
        stop

    download/inspect if exact manifest not yet known
    recompute candidate slots
    simulate planner with accepted tentative set

    if planner blocked:
        reject with evidence
        continue

    if marginal coverage fell to zero and policy is gap-only:
        reject/skip as redundant
        continue

    accept
    covered += candidate slots
```

The stable identity tie-break is essential for reproducible plans/tests.

---

# Planner integration

Do not modify planner conflict semantics to perform bulk selection.

Instead build a separate simulation input around the existing planner.

The simulation needs:

- currently installed mod descriptors/files;
- tentative accepted candidate descriptors/files;
- same persisted rules/family/resource preferences;
- candidates marked enabled only inside the simulation;
- no changes to the real database or live manifest.

The output can be examined for blocking conflicts.

Only after acceptance should the ordinary import/persistence workflow publish the candidate.

This keeps:

- selection policy;
- deployment safety;
- persistence;

as separate boundaries.

---

# Semantic slot extraction for remote candidates

The source provider does not need to know MHW model IDs.

After download and safe extraction:

1. normalize archive structure;
2. enumerate deployable candidate paths;
3. use the same path normalization rules as `ModScanner`;
4. call `PathRules.TryGetArmorComponent`;
5. collect distinct `(modelId, component)`;
6. preserve exact paths for planner simulation.

This guarantees the optimizer and installed-library index use the same semantic parser.

Do not write a second regex in the remote subsystem.

---

# Existing asset bundle behavior is useful but not enough

`AssetBundles.KeyForPath` already maps structural armor files to:

```
armor:<modelId>:<component>
```

This is exactly the right conceptual unit for coherent structural choice.

However:

- texture paths may remain file-granular;
- coverage is about whether a slot has any provider, not only conflict bundling;
- a candidate may contain several structural files and textures within one slot.

Therefore reuse the semantic key concept but do not derive coverage solely from the current conflict bundle list.

---

# User-facing coverage model

Recommended summary:

```
Physical models covered: 143 / 270 known models
Physical components covered: 412 / ? authoritative slots
Complete 5-piece models: 51
Partially covered models: 92
Alternative-only candidates skipped: 37
```

Until an authoritative denominator exists, do not display:

```
412 / 1350
```

as definitive.

Prefer:

```
412 observed/known physical component slots covered
```

or an adapter-provided denominator with source/version.

---

# Coverage page improvements for later checkpoints

The current Coverage page is useful but should eventually distinguish:

- physical model ID;
- display aliases;
- provider count per component;
- effective provider per component;
- missing components;
- complete vs partial status;
- number of alternative providers;
- preview from effective or highest-ranked provider.

Example row:

```
Odogaron / pl029_0000
Head: 2 available / active Mod A
Chest: missing
Arms: 1 available / active Mod B
Waist: missing
Legs: 3 available / inactive
Physical coverage: 3/5
```

The display may list aliases separately without multiplying the underlying coverage denominator.

---

# Important current projection issue

Current SQL:

```
COUNT(DISTINCT mfa.mod_id)
```

is model-wide.

If one mod supplies only chest and another only legs, the row reports two available providers but does not tell the user:

- which component has which provider;
- whether head/arms/waist are empty;
- whether both mods overlap the same component.

For the future optimizer, use a component-grain projection.

Do not overload the meaning of `AvailableProviders`.

---

# Physical model aliases

Because many catalog rows point to one model ID, introduce explicit alias handling.

Recommended behavior:

- one physical model row is canonical for optimization;
- UI can show `Aliases: Leather, Skull, Bushi "Sabi", ...`;
- search by any alias resolves to the physical model;
- selecting one alias for a bulk-fill target should warn/indicate that the underlying replacement may affect every in-game item sharing that model.

This is especially important: users may think they are downloading a mod "for one armor name" when the physical model ID is shared.

The manager already has enough catalog data to surface this risk.

---

# Duplicate catalog row tests

The canonical dataset itself should become a regression fixture.

Tests should prove:

- `pl001_0000` aliases do not create 24 independent physical capacity slots;
- one `pl001_0000 chest` provider increments physical coverage once;
- catalog display can still show all matching aliases;
- two aliases sharing a model receive the same physical slot state.

---

# Candidate deduplication

Coverage optimizer deduplication must operate at multiple layers.

## Remote identity duplicate

Same provider/file encountered twice.

Drop duplicate before download.

## Artifact duplicate

Different remote identities produce same archive SHA-256.

Avoid repeated extraction/import.

## Physical coverage duplicate

Different mods provide the same physical armor slots.

Do not call them identical.

They are alternatives.

In gap-fill mode, the later one has low or zero marginal coverage.

## Exact file duplicate

Planner already handles byte-identical same-path providers safely.

---

# Full-set and partial-set behavior

A candidate that contains:

```
head + chest + arms + waist + legs
```

for one model is a full-set candidate.

A candidate that contains only:

```
chest
```

is a partial candidate.

The optimizer should not assume full sets are always better.

Policy examples:

- maximize model variety -> prefer single-piece mods across empty models;
- complete sets -> prefer full/near-complete candidates;
- chest-only collection -> ignore other component coverage utility;
- minimum downloads -> full sets get a package-efficiency bonus.

This belongs in policy, not hardcoded MHW semantics.

---

# Package efficiency

Useful optional metric:

```
newSlots / downloadBytes
```

or:

```
newModels / downloadBytes
```

This can matter for bulk fill.

Do not make it the default primary rank because tiny low-quality packages could dominate.

Treat it as a user-selectable tie-break/budget optimization.

---

# Popularity vs coverage

A common failure mode would be:

1. sort all mods by popularity;
2. download until count budget reached.

That will heavily cluster around already-popular armor/model replacements and leave most catalog space empty.

For the user's specific "fill available data" goal:

- coverage novelty should usually beat fallback popularity;
- favorite authors/tags can intentionally override this;
- pure redundant candidates should normally be skipped in gap-fill mode.

Recommended lexicographic order:

```
hard safety/compatibility
explicit user favorite-author/tag tier
marginal semantic coverage
source/user rank
popularity
stable identity
```

Provide a toggle if the user wants popularity before coverage.

---

# Confidence and unknown semantics

Candidates may contain armor-like content the current parser does not understand.

Examples:

- unexpected package layout;
- unsupported model naming;
- installer-generated paths;
- files requiring FOMOD selection before final path set is known.

Do not score these as zero-risk non-armor mods.

Use a state:

```
KnownCoverage
PartialCoverage
UnknownCoverage
NotArmor
```

For unattended MHW coverage fill:

- `KnownCoverage` accepted normally;
- `PartialCoverage` can be policy-controlled;
- `UnknownCoverage` requires review;
- `NotArmor` does not consume the armor fill target.

---

# FOMOD / selectable packages

PR #1 contains broader FOMOD work that is not canonical main at this audit base.

If/when installer selection becomes canonical, slot evaluation must occur on the **selected install result**, not every possible FOMOD branch combined.

Otherwise a candidate may appear to cover mutually-exclusive alternatives simultaneously.

The coverage layer should consume a concrete resolved candidate file manifest.

It should not parse installer decision trees itself.

---

# Interaction with logical families

Existing family inference/composition is valuable.

If a candidate is another package from the same logical family as an already-selected mod:

- it may be a required/optional component;
- it may intentionally overwrite some family paths;
- it may add physical slots or merely alter one slot.

Therefore do not reject same-slot overlap solely by family.

Use:

- planner/family semantics for safety;
- marginal physical coverage for fill utility.

A family component adding no new slot can still be necessary for the selected package's intended appearance, but it should be treated as a dependency/component rather than another gap-filling candidate.

---

# Persistence direction

Do not add new tables in the first checkpoint.

The first implementation can compute physical slot state from existing:

- `armor_catalog`;
- `mod_file_armor`;
- `mods`;
- planner snapshot.

Later, if persistent user targets are required, consider a small policy/state store for:

- selected target models;
- excluded slots/components;
- preferred completion mode;
- user-accepted alternatives.

Any schema change must follow the existing storage transaction audit and be isolated from provider/download work.

---

# Recommended implementation checkpoints

## Checkpoint 1 — pure physical-slot coverage calculator

No schema.
No network.
No WPF.
No production query replacement.

Add pure domain/calculation support that accepts:

- catalog aliases;
- local slot providers;
- target universe if available.

Produces:

- canonical physical models;
- alias mapping;
- per-slot provider sets;
- complete/partial model metrics.

Tests use synthetic data plus duplicate aliases.

This is the safest first source checkpoint.

## Checkpoint 2 — component-grain read projection

Add a read-only repository projection exposing:

```
modelId
component
provider mods
effective provider
aliases
```

Do not alter transaction owners.

Preserve the current Coverage page until parity is proven.

## Checkpoint 3 — candidate marginal-coverage evaluator

Pure calculation:

- novel slots;
- redundant slots;
- new model count;
- completion count;
- deterministic utility breakdown.

No download logic.

## Checkpoint 4 — tentative greedy selector with synthetic candidates

Add policy and deterministic recomputation after every accepted candidate.

Planner-blocked status can be supplied as an input/fake at first.

Tests should prove ordering and recomputation.

## Checkpoint 5 — planner simulation adapter

Feed captured candidate manifests into the existing planner without persisting tentative mods.

This checkpoint needs careful design around snapshot composition but should not change planner conflict semantics.

## Checkpoint 6 — connect to source-neutral acquisition

Only after the remote provider contracts from the separate multi-source audit are integrated.

Remote ranking + MHW marginal coverage become two inputs to one explainable selector.

## Checkpoint 7 — Coverage UI enhancement

Only after the underlying model is proven.

Expose:

- missing pieces;
- physical vs alias identity;
- alternatives;
- bulk-fill target selection;
- plan preview.

---

# Test matrix

## Alias/canonicalization

- one model / one alias;
- one model / many aliases;
- different names sharing one model;
- duplicate CSV rows;
- search alias resolves same physical state.

## Slot extraction

- each of five known component directories;
- mixed slash/casing;
- invalid path;
- model ID absent;
- model-like token outside valid segment;
- multiple files in one component;
- one mod spanning multiple models.

## Coverage state

- no providers;
- one provider;
- multiple alternative providers;
- partial model;
- complete model;
- effective provider absent;
- effective provider present;
- deployed component provider differs from highest-priority available alternative.

## Marginal candidate evaluation

- all novel;
- all redundant;
- mixed;
- completes model;
- opens new model;
- duplicates same slot across several exact files;
- non-armor content only;
- unknown semantic content.

## Greedy recomputation

- candidate B loses novelty after A accepted;
- tie broken by user tier;
- tie broken by stable identity;
- popular redundant candidate loses to lower-popularity novel candidate in gap-fill mode;
- favorite-author redundant candidate wins only when configured policy says favorites outrank coverage.

## Planner integration

- no exact conflicts;
- blocking structural conflict;
- identical bytes;
- known overlay;
- same-family option;
- texture resolution;
- semantic redundant but planner-safe candidate is skipped for zero coverage in gap mode, not labeled incompatible.

---

# Acceptance criteria for the first production slice

The recommended first production slice should satisfy all of these:

1. one physical model ID is counted once regardless of catalog alias count;
2. one physical component slot is identified by `modelId + component`;
3. provider counts are component-specific;
4. no assumption that every model has all five components is presented as fact;
5. coverage metrics distinguish available/library from effective/deployed;
6. calculation is deterministic;
7. calculation has no WPF dependency;
8. calculation has no network dependency;
9. no database schema change;
10. no planner behavior change;
11. tests include real duplicate-alias shapes from the bundled catalog;
12. exact verification/handoff rules are followed.

---

# Important non-goals

Do not combine this work with:

- new provider HTTP adapters;
- download queue implementation;
- arbitrary web scraping;
- WPF page decomposition;
- ManagerDatabase decomposition;
- changing DeploymentPlanner conflict semantics;
- changing family inference;
- schema migrations;
- transaction-boundary repairs;
- async lifetime coordinator work;
- FOMOD implementation;
- authoritative vanilla-slot reverse engineering unless separately scoped.

---

# Specific durable conclusions

1. The current Coverage page should **not** be used directly as the bulk-fill capacity model.
2. `series_id/name` rows are display aliases; `model_id + component` is the physical semantic identity.
3. The current CSV has substantial many-to-one aliasing and will inflate any row-based fill percentage.
4. `mod_file_armor` already provides the correct local semantic grain for provider analysis.
5. `AssetBundles` independently confirms that `model + component` is already treated as an important MHW atomic unit.
6. Planner exact-path safety and semantic coverage utility must remain separate.
7. A redundant armor alternative is not automatically a conflict.
8. Candidate marginal coverage must be recomputed after every accepted candidate.
9. The complete slot denominator is currently uncertain; do not market `270*5` as exact.
10. The first implementation should be a pure coverage calculator, not a downloader.

---

# Integration notes for parallel work

At audit time canonical main remains:

`6ada5a5c4cc83afadfba42bc6af6559540920e3d`

Open support/product PRs include:

- PR #1 broader mod workflow/FOMOD work;
- PR #2 MainWindow/UI architecture audit;
- PR #3 verification/supply-chain audit;
- PR #4 test/failure/performance audit;
- PR #5 multi-source discovery/safe bulk-fill audit;
- PR #6 async lifetime/cancellation/shutdown audit.

This support lane is intentionally documentation-only and independent.

If PR #1 or later work changes FOMOD resolution, coverage must consume the resolved concrete file set rather than installer branches.

If PR #5 provider contracts land, this audit supplies the MHW-specific semantic utility layer that ranks otherwise-compatible remote candidates.

---

# Successor handoff

A successor implementing this design must:

1. verify actual canonical `main` before editing;
2. read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and the remaining `README_FIRST.md` order;
3. inspect every support audit merged since this document was written;
4. preserve the distinction between catalog aliases, physical slots, exact paths, and logical families;
5. never count catalog aliases as independent physical replacement capacity;
6. never turn semantic redundancy into a fake planner incompatibility;
7. never claim the full five-component universe is authoritative without adapter data proving it;
8. implement one independently verifiable checkpoint only;
9. update durable handoff/context during work;
10. commit and push stable checkpoints;
11. explicitly require the next successor to inherit, preserve, and recursively propagate the permanent continuity constitution to the agent after them.

**Do not break the chain.**
