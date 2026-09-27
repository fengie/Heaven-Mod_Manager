# Multi-source mod discovery and bulk-fill architecture audit

Date: 2026-09-27  
Support role: independent architecture/research support agent  
Canonical base inspected: `fengie/mhw-mods` `main` at `6ada5a5c4cc83afadfba42bc6af6559540920e3d`

## Support prompt used

> Continue from canonical `fengie/mhw-mods` `main`. Read the permanent repository rules and current handoff state first. Inspect the current remote metadata, archive import, Smart Inbox, catalog/provenance, recipes, game profile, planner/conflict, and update-diff paths. Compare that state with the user's intended future feature: source mods from many providers, rank by favorite authors/tags/popularity, fill remaining capacity with popular mods, require correct game/version compatibility, and bulk-acquire only combinations that are safe enough to accept unattended. Do not modify production source, active storage transaction boundaries, WPF ownership, verifier/CI logic, or the currently closed PlannerSnapshotRepository boundary. Produce a durable, implementation-oriented architecture audit with provider capability boundaries, normalized data contracts, ranking/selection semantics, exact-vs-heuristic compatibility rules, download/import staging, cancellation/retry/rate-limit behavior, persistence seams, test strategy, and small independent implementation checkpoints. Keep the permanent recursive continuity rules intact and explicitly pass them to the successor.

## Why this was chosen

The currently active support work already covers:

- deep SQLite transaction atomicity;
- CI/verification/supply-chain integrity;
- MainWindow/WPF responsibility ownership;
- test gaps, failure modes, and performance risks.

The current canonical source boundary, `PlannerSnapshotRepository`, is closed and Windows-verified. Starting another storage extraction or another MainWindow split would duplicate or interfere with existing work.

This audit therefore targets a separate future product capability that the existing code is already partially prepared for but does not yet provide: **remote discovery + source-neutral acquisition + policy-driven bulk fill**.

No production C#, XAML, schema, verification cache, or workflow file is changed by this support task.

---

# Executive finding

The repository already contains most of the **post-download** machinery needed for safe multi-source acquisition:

- safe archive inspection/extraction;
- quarantined/staged import;
- automatic catalog registration;
- source-file capture and hashing;
- provenance/version intelligence;
- logical family/supersession inference;
- collection recipe export;
- planner-backed exact file-conflict resolution;
- game-build fingerprinting;
- update diffing;
- Smart Inbox automation.

What is missing is the layer **in front** of those systems:

1. a source-neutral remote provider contract;
2. normalized candidate metadata;
3. game/version compatibility evidence;
4. source capability/rate-limit modeling;
5. user policy ranking;
6. acquisition queue/persistence;
7. pre-download risk filtering;
8. exact post-download conflict simulation;
9. acceptance/rejection of candidate combinations;
10. resumable downloading and provenance handoff into the existing importer.

The most important architectural fact is:

> The manager cannot truthfully guarantee that two arbitrary remote mods are non-conflicting before it has a reliable file manifest for both. For most sources, the exact manifest is only known after downloading and inspecting the archive.

Therefore bulk fill should use a **two-stage acceptance model**:

- **Stage A — pre-download eligibility:** cheap metadata/version/dependency/source-policy filters and ranking.
- **Stage B — post-download exact acceptance:** safe extraction/capture into quarantine, exact file manifest construction, then planner/conflict simulation against the installed library plus already-selected candidates.

A candidate that passes Stage A is only *eligible to inspect*, not yet safe to install.

---

# Existing repository seams

## 1. Nexus integration is an enrichment service, not a general source layer

`src/MhwModManager.Filesystem/NexusMetadataService.cs` currently:

- infers Nexus identity from local sidecars, archive/folder names, and URLs;
- reads `NEXUS_API_KEY` or `nexus-api-key.txt`;
- queries Nexus v3 metadata;
- downloads preview images;
- performs bounded public-page artwork fallback;
- performs update checks;
- fills provenance;
- infers Nexus families;
- rebuilds supersession links.

Its constructor and domain types are Nexus-specific:

- `GameProfile.NexusGameDomain`;
- `ModDescriptor.NexusModId`;
- `ModDescriptor.NexusFileId`;
- `ModProvenance.Nexus*`;
- `NexusFileCategory`;
- `ProvenanceSource.NexusApi`.

This is useful existing intelligence but it should not become the abstraction for all remote providers. A future source-neutral layer should wrap or feed this behavior rather than growing `NexusMetadataService` into a giant provider switch.

## 2. ArchiveImportService is a strong final ingestion boundary

`src/MhwModManager.Automation/ArchiveImportService.cs` already owns a clean local archive workflow:

1. inspect archive;
2. reject suspicious paths;
3. extract into a temporary `.importing` directory;
4. normalize a single wrapper directory;
5. atomically publish by directory move;
6. refresh catalog.

It deliberately does not touch live game files.

This should remain the final library-publication boundary for downloaded archives.

A remote acquisition subsystem should download to a separate acquisition cache, verify the artifact, and then invoke the existing import path or a narrowly extended import overload that carries source metadata.

It should **not** stream remote bytes directly into the mod library.

## 3. Smart Inbox proves automation can remain post-download and source-agnostic

`SmartInboxService` already:

- scans files/folders;
- rejects unsupported/unsafe archives;
- imports into the library;
- classifies;
- refreshes the catalog;
- captures source files;
- runs Nexus metadata enrichment.

This is conceptually close to the second half of a remote acquisition pipeline.

The future remote subsystem should not duplicate Smart Inbox or ArchiveImport logic. It should converge on the same safe ingestion/capture path.

## 4. CatalogService is intentionally filesystem-derived

`CatalogService.RefreshFoldersAsync` creates a local mod descriptor from a newly published source directory and infers Nexus IDs from naming conventions.

For remote acquisition this implies two choices:

- keep catalog discovery as the source of local mod identity, then attach remote identity afterward; or
- narrowly extend import/catalog to accept a source metadata envelope when the archive is published.

The second is cleaner long-term because renamed archives and normalized directories should not be the only link back to a remote source.

Do not change this in the first checkpoint.

## 5. Collection recipes already preserve some remote identity

`CollectionRecipeService` exports:

- `SourceUrl`;
- Nexus IDs/version fields;
- family/category state;
- enabled/priority state.

This is evidence that remote identity is already part of the product model.

A future recipe format can reference normalized source identities without redistributing mod payloads.

## 6. The planner is the exact acceptance oracle after file capture

`DeploymentPlanner` reasons over:

- enabled mod descriptors;
- captured files;
- explicit rules;
- family/auto-compatibility logic;
- current manifest/originals.

This is the strongest existing mechanism for exact overlap/conflict behavior.

For bulk acquisition, do not create a separate competing "remote conflict algorithm." Instead create a **candidate simulation snapshot** after an archive has been safely inspected/captured.

The remote selector may use metadata heuristics before downloading, but final unattended acceptance should reuse planner semantics.

## 7. Game compatibility already has a durable anchor

`GameProfile` provides:

- stable game/profile identity;
- executable path;
- Steam App ID;
- store;
- Nexus domain;
- game root/live mod root;
- generic vs enhanced support tier.

The database also has game-build fingerprints.

This is enough to build source-provider game mappings and compatibility evidence without making a new global game identity system first.

---

# Recommended source-neutral model

Do not immediately replace the current Nexus fields. Introduce a parallel normalized remote-discovery model first.

Suggested contracts:

```csharp
public interface IRemoteModSource
{
    string SourceId { get; }
    RemoteSourceCapabilities Capabilities { get; }

    IAsyncEnumerable<RemoteModCandidate> DiscoverAsync(
        RemoteDiscoveryQuery query,
        CancellationToken ct);

    Task<RemoteModDetails?> GetDetailsAsync(
        RemoteModIdentity mod,
        CancellationToken ct);

    Task<IReadOnlyList<RemoteFileCandidate>> GetFilesAsync(
        RemoteModIdentity mod,
        CancellationToken ct);

    Task<ResolvedDownload?> ResolveDownloadAsync(
        RemoteFileIdentity file,
        CancellationToken ct);
}
```

Provider methods should expose only capabilities the provider can actually support. Do not require every source to implement direct download.

Suggested capability flags:

- `CanDiscover`
- `CanSearch`
- `CanDirectDownload`
- `RequiresAuthentication`
- `CanReadAuthor`
- `CanReadTags`
- `CanReadPopularity`
- `CanReadVersion`
- `CanReadGameVersionCompatibility`
- `CanReadDependencies`
- `CanReadFileManifestBeforeDownload`
- `CanReadChecksums`
- `CanReadUpdateLineage`

Suggested normalized identity:

```csharp
public sealed record RemoteModIdentity(
    string SourceId,
    string GameKey,
    string ModKey);

public sealed record RemoteFileIdentity(
    RemoteModIdentity Mod,
    string FileKey);
```

Never use display name as identity.

Suggested normalized candidate:

```csharp
public sealed record RemoteModCandidate(
    RemoteModIdentity Identity,
    string DisplayName,
    string? AuthorKey,
    string? AuthorName,
    IReadOnlySet<string> Tags,
    Uri PageUrl,
    string? Version,
    DateTimeOffset? UpdatedAt,
    RemotePopularity Popularity,
    RemoteCompatibilityEvidence Compatibility,
    IReadOnlyList<RemoteDependency> Dependencies,
    RemoteSourceCapabilities Capabilities);
```

Popularity must retain raw dimensions rather than one source-specific score:

- downloads;
- unique downloads if available;
- likes/endorsements;
- views;
- recency;
- source rank;
- source-provided trending signal.

Normalize for cross-source ranking later. Do not pretend "10,000 downloads" means the same thing on every provider.

---

# Source capability policy

A provider adapter is not merely an HTTP client. It must declare what the source permits and what evidence it can produce.

## Nexus

Current official Nexus API documentation exposes a v3 API. The repository already uses it for metadata. Nexus also enforces public API request quotas; the current official help article states a daily public API quota and a reduced hourly quota after the daily threshold.

Implications for this manager:

- centralize per-source request throttling;
- read quota/reset response headers;
- coalesce duplicate metadata requests;
- cache discovery/details separately from local mod provenance;
- do not run one metadata request tree per candidate during ranking;
- only resolve downloads for candidates that survive selection;
- never log API keys;
- do not rely on public HTML scraping for core discovery/download behavior.

Official references researched 2026-09-27:

- https://api-docs.nexusmods.com/
- https://help.nexusmods.com/article/105-i-have-reached-a-daily-or-hourly-limit-api-requests-have-been-consumed-rate-limit-exceeded-what-does-this-mean

## GameBanana

GameBanana exposes structured APIs that can return submission data and fields including authors, downloads, files, game, preview, update, and URL information. That makes it a plausible direct structured adapter.

Treat the exact fields/endpoints as provider-versioned behavior and cover them with contract fixtures instead of scattering JSON parsing through UI/services.

Official references researched 2026-09-27:

- https://api.gamebanana.com/
- https://gamebanana.com/apiv11/Mod/Index

## GitHub Releases

GitHub's release API can list releases and assets. Public release assets can be downloaded without authentication; asset responses expose names, sizes, URLs and, where present, digest information.

GitHub is a strong first source adapter for architecture validation because:

- API behavior is documented;
- release assets are explicit files;
- downloads are source-neutral archives/binaries;
- no HTML scraping is required;
- release metadata and tags are available.

But GitHub cannot automatically determine whether an arbitrary repository is actually a mod for the current game. Discovery should therefore be allow-listed, recipe-linked, author-linked, or otherwise anchored to a known repository identity rather than broad global GitHub search in the first implementation.

Official references researched 2026-09-27:

- https://docs.github.com/en/rest/releases
- https://docs.github.com/en/rest/releases/assets

## Mod DB

Mod DB officially exposes RSS feeds for releases/downloads and encourages their use with attribution. This is suitable for **discovery**.

Do not assume an official unrestricted binary-download API exists. Until a permitted stable download path is explicitly verified, model Mod DB as:

- discovery/details capable;
- direct-download capability false or conditional;
- UI can link the user to the source page for acquisition.

Official reference researched 2026-09-27:

- https://www.moddb.com/rss

## Unknown/arbitrary websites

Do not implement "scrape any site" as the abstraction.

Each source adapter should have explicit:

- URL ownership;
- rate limit policy;
- authentication mechanism;
- terms/robots expectations;
- download-resolution behavior;
- parser fixtures;
- failure semantics.

A source that requires login, anti-bot bypass, premium entitlement, unstable HTML scraping, or unclear permission should be link-out/manual until a compliant adapter is designed.

---

# Compatibility model

"Works with this game" and "works with this game version" are separate questions.

Suggested compatibility evidence dimensions:

1. **game identity**
   - exact provider game ID mapping;
   - Steam App ID mapping;
   - explicit recipe/source mapping;
   - manual mapping.

2. **game build/version**
   - provider-declared supported version/build;
   - release date relative to current build;
   - explicit author notes;
   - dependency version requirements;
   - known loader/framework requirement.

3. **package shape**
   - archive contains paths valid for the active `GameProfile`;
   - package uses a known adapter namespace;
   - executable/installer payload requires review;
   - archive is empty or malformed.

4. **post-extraction structural evidence**
   - exact destination paths;
   - file classes;
   - loader/plugin markers;
   - duplicate or incompatible framework files.

Compatibility should have a state, not merely a score:

- `Compatible`
- `LikelyCompatible`
- `Unknown`
- `Incompatible`
- `RequiresUserReview`

For unattended bulk fill, default policy should accept only `Compatible` or sufficiently strong `LikelyCompatible` candidates. `Unknown` should not silently become yes.

A source's popularity must never override an incompatible/unknown hard gate.

---

# Bulk-fill ranking semantics

The user's intended policy is naturally represented as **lexicographic preference tiers followed by a fallback rank**, not one giant opaque score.

Example policy:

1. hard constraints:
   - correct game;
   - compatible build/version;
   - permitted source;
   - acceptable content/category;
   - not already installed/equivalent;
   - archive/download size under limits;
   - required dependencies satisfiable.

2. preferred-author tier:
   - explicit favorite authors first.

3. preferred-tag tier:
   - candidates matching user-selected tags.

4. source/user weighting:
   - source preference;
   - minimum endorsement/download threshold;
   - recency constraints.

5. fallback:
   - most popular remaining candidate.

6. post-download exact acceptance:
   - inspect/capture;
   - exact conflict simulation;
   - reject blocking combinations;
   - continue with next ranked candidate until target is filled or candidate pool is exhausted.

This keeps the policy explainable: every accepted mod can report *why it was selected*.

Suggested decision record:

```csharp
public sealed record CandidateSelectionReason(
    string Rule,
    int Tier,
    string Explanation,
    IReadOnlyDictionary<string,string> Evidence);
```

The UI should show these reasons instead of a magic score.

---

# "Fill the remaining slots" should be a constrained selection problem

Do not define a "slot" as only mod count.

Useful limits may include:

- maximum accepted mod count;
- maximum total compressed download bytes;
- maximum extracted bytes;
- maximum number of candidates per tag/category;
- target catalog coverage;
- maximum unresolved review items;
- source-specific request/download budget.

For MHW outfit/catalog use cases, a stronger target is:

> add candidates that increase uncovered asset/catalog coverage without creating blocking conflicts.

That requires a provider or downloaded archive to expose enough file/asset evidence.

After download/capture, compute each candidate's **marginal coverage gain** and exact conflict cost.

A practical greedy selector can then choose the highest-ranked candidate that:

- passes hard compatibility;
- fits remaining budgets;
- increases requested coverage or satisfies a preferred policy tier;
- creates no blocking planner decision with the tentative set.

Do not start with a global NP-hard optimizer. An explainable greedy loop with exact post-download planner checks is appropriate and testable.

---

# Exact non-conflict acceptance

## Why metadata alone is insufficient

Two mods with different:

- names;
- authors;
- tags;
- Nexus categories;
- source pages;

may overwrite the same game file.

Conversely, two mods from the same page/family may be intentionally composable.

Therefore source metadata is not enough to prove non-conflict.

## Recommended acceptance loop

For each ranked candidate:

1. resolve one concrete remote file;
2. download to acquisition cache;
3. verify expected size/checksum when available;
4. run `ArchiveInspector`;
5. extract into **candidate quarantine**, not the live library;
6. normalize wrapper;
7. scan/capture a temporary file manifest;
8. build a tentative planner snapshot containing:
   - existing installed library;
   - already accepted candidates;
   - this candidate;
9. run existing conflict/planner logic;
10. if blocking:
    - reject candidate;
    - record exact paths/reasons;
    - keep or evict cache by policy;
11. if non-blocking:
    - publish through `ArchiveImportService`/catalog;
    - persist source identity;
    - leave mod disabled unless the user explicitly asked for a staged profile/loadout workflow;
12. continue until fill target reached.

The first implementation does not need parallel candidate simulation. Correctness and determinism matter more.

---

# Download and cache boundary

Suggested layout:

```
<stateRoot>/
  acquisition/
    cache/
      <sourceId>/
        <gameKey>/
          <modKey>/
            <fileKey>/
              artifact
              artifact.sha256
              metadata.json
    staging/
      <jobId>/
    rejected/
      <jobId>/
```

Rules:

- never download directly into `modsRoot`;
- use a unique temp file then atomic rename after successful download;
- enforce maximum response size when known;
- enforce maximum extracted size/file count in archive inspection;
- preserve original source filename separately from sanitized local path;
- hash every downloaded artifact;
- if provider exposes a checksum/digest, verify it;
- cache key is provider identity + remote file identity + immutable version/digest where possible;
- a resumed download must prove it is the same remote file before appending;
- cancellation must leave either a clearly resumable partial object or a disposable temp object, never a half-published mod.

---

# Persistence seam

Do not immediately add provider fields to `mods`.

A cleaner future schema is source-neutral and many-to-one capable:

```sql
remote_mod_identity(
    mod_id,
    source_id,
    game_key,
    remote_mod_key,
    remote_file_key,
    remote_version,
    page_url,
    metadata_json,
    confidence,
    updated_at
)

acquisition_jobs(
    id,
    game_profile_id,
    policy_json,
    state,
    created_at,
    updated_at,
    error
)

acquisition_items(
    job_id,
    source_id,
    remote_mod_key,
    remote_file_key,
    rank,
    state,
    rejection_reason,
    artifact_sha256,
    local_mod_id
)
```

This is only a design direction.

Because storage transaction ownership is under separate active audit and because `PlannerSnapshotRepository` just closed, schema work should be a later isolated checkpoint with an explicit transaction map.

Do not mix it into provider-contract work.

---

# Source identity and deduplication

Use progressively stronger identity:

1. exact same provider + remote mod/file identity;
2. provider-declared immutable version/file identity;
3. exact downloaded archive SHA-256;
4. exact captured content/file-set hash;
5. high-confidence cross-source equivalence only with explicit evidence.

Never auto-deduplicate two candidates solely because:

- display names match;
- authors match;
- filenames look similar.

Cross-posted mods can legitimately diverge by version or packaging.

---

# Dependencies

Remote dependencies need their own normalized graph.

A candidate can declare:

- required mod;
- required framework/loader;
- optional dependency;
- incompatible mod;
- minimum/maximum version.

Before download, dependency resolution can use source metadata.

After extraction, exact compatibility still goes through existing file/planner checks.

For unattended fill:

- unsatisfied required dependency => candidate not eligible unless resolver can enqueue the dependency;
- dependency cycle => reject and explain;
- dependency version unknown => review-required;
- optional dependency does not become required simply because it is popular.

---

# Rate limits and concurrency

The current Nexus metadata flow is deliberately sequential and throttled by coarse timestamps. That is acceptable for installed-library enrichment but not enough for multi-source discovery.

Add a source-level scheduler later:

- per-source max concurrency;
- request-per-window quota;
- Retry-After support;
- exponential backoff with jitter;
- quota/reset metadata;
- circuit breaker after repeated provider failures;
- cancellation propagated through every network operation.

Separate:

- metadata request concurrency;
- download concurrency;
- extraction/hash concurrency.

A reasonable first implementation should use very small defaults, e.g. one or two downloads at once, and keep archive extraction/hash concurrency bounded to avoid SSD saturation. The repository already documents the same principle in `CatalogService.EnsureCapturedAsync`.

---

# Security and trust boundaries

Remote acquisition expands the attack surface substantially.

Required rules:

- downloaded content is untrusted;
- retain `ArchiveInspector` path traversal/device-path checks;
- add extracted-size/file-count/ratio limits before unattended bulk mode;
- never execute installers/scripts automatically;
- executable/DLL/PowerShell/batch content should be classified for review according to game adapter policy;
- never accept TLS errors;
- never send one provider's credentials to another provider;
- secrets belong in provider-specific secret storage, not logs or exported recipes;
- redirects must be bounded and validated;
- reject non-HTTP(S) remote download schemes unless explicitly supported;
- content-type is advisory, not trusted;
- inspect by bytes/archive parser rather than filename extension alone where practical.

Popularity is not a security signal.

---

# UI/product semantics

The first user-facing workflow should be a **plan and preview**, not "download 500 mods now."

Suggested flow:

1. choose game;
2. choose enabled sources;
3. set hard filters;
4. set preferred authors/tags;
5. set count/size/coverage target;
6. preview ranked candidates and why;
7. start acquisition;
8. show states:
   - discovered;
   - filtered;
   - queued;
   - downloading;
   - inspecting;
   - exact-conflict checking;
   - accepted;
   - rejected with reason;
   - requires review;
9. final summary.

Every rejected candidate should have a concrete reason such as:

- wrong game;
- unsupported current build;
- missing dependency;
- duplicate artifact;
- unsafe archive;
- blocking overlap with X on paths A/B/C;
- provider download unavailable;
- quota exhausted;
- manual login required.

This makes bulk fill auditable rather than magical.

---

# Provider adapter checkpoint order

## Checkpoint 1 — contracts + fake provider only

Production scope:

- normalized remote models;
- `IRemoteModSource`;
- capability flags;
- deterministic rank/filter policy;
- fake/in-memory provider.

No real network.
No schema.
No UI.
No downloads.

Tests:

- hard filters always beat rank;
- favorite author/tag tiers are deterministic;
- source popularity metrics are normalized only through explicit policy;
- cancellation;
- duplicate remote identities;
- unknown compatibility fails unattended policy.

This is the safest first code checkpoint.

## Checkpoint 2 — GitHub Releases adapter + download cache

Why first:

- well-documented API;
- public assets can be fetched without browser scraping;
- explicit release/file identities;
- useful digest metadata where available.

Scope:

- known/allow-listed repositories only;
- list releases/assets;
- resolve asset download;
- bounded cache downloader;
- no broad GitHub discovery.

Still do not change local mod provenance schema.

## Checkpoint 3 — quarantine import + exact candidate simulation

Add:

- candidate extraction area;
- temporary scanning/capture;
- planner simulation;
- rejection evidence.

This is the checkpoint that makes "non-conflicting bulk fill" truthful.

## Checkpoint 4 — acquisition job persistence/resume

Only after storage audit guidance is incorporated.

Persist:

- policy;
- discovered identity;
- ranking;
- state transitions;
- artifact digest;
- acceptance/rejection reason.

Do not make filesystem + DB state transitions independently commit without an explicit recovery design.

## Checkpoint 5 — Nexus source adapter

Refactor source-discovery/download behavior behind provider contracts while preserving existing installed-mod metadata enrichment.

Add:

- quota header handling;
- batched/cached details;
- direct-download capability only where account/API rules permit;
- provider-specific authentication.

Do not regress current `NexusMetadataService` provenance/update behavior.

## Checkpoint 6 — GameBanana adapter

Use structured API fields for:

- author;
- game;
- tags/category;
- popularity;
- files/download;
- update metadata.

Provider contract fixtures should protect against field shape drift.

## Checkpoint 7 — Mod DB discovery adapter

Start discovery-only using supported RSS and attribution.

Keep direct download false until a stable permitted download mechanism is explicitly verified.

---

# Test plan

## Provider contract tests

For every provider:

- canonical game mapping;
- identity stability;
- pagination;
- empty page;
- duplicate result across pages;
- malformed response;
- partial metadata;
- rate-limit response;
- auth failure;
- redirect;
- cancellation;
- provider timeout.

Use recorded/sanitized fixtures where provider terms permit; otherwise deterministic synthetic fixtures.

## Ranking tests

- favorite author beats fallback popularity;
- preferred tag tier ordering;
- multiple matching priorities;
- same candidate from multiple search queries deduplicates;
- unknown compatibility excluded from unattended mode;
- source disabled excludes all source candidates;
- deterministic tie-breaker independent of enumeration order.

## Download tests

- truncated body;
- wrong content length;
- checksum mismatch;
- redirect loop;
- disk full;
- cancellation during stream;
- resumable partial mismatch;
- same artifact requested twice;
- provider reports asset larger than configured cap.

## Archive/import tests

- traversal/device path;
- zip bomb / extraction budget;
- huge file count;
- unsupported format;
- wrapper normalization;
- duplicate destination;
- cancellation before publish;
- import remains disabled.

## Exact conflict tests

- two remote candidates no overlapping paths;
- exact overwrite conflict;
- intentional composable family;
- explicit user conflict rule;
- third-provider overlap;
- candidate conflict with already installed enabled mod;
- candidate conflict only with disabled installed mod;
- same candidate accepted/rejected deterministically regardless discovery order.

## Compatibility tests

- exact supported game/build;
- wrong game;
- explicit incompatible build;
- unknown build;
- required dependency present;
- required dependency queued;
- required dependency unavailable;
- dependency cycle.

---

# Performance rules

Do not fetch detailed metadata for every search result before ranking.

Suggested staged fan-out:

1. provider discovery returns compact candidates;
2. normalize and discard obvious failures;
3. cheap rank;
4. fetch details only for top N per policy tier;
5. resolve files only for candidates still in consideration;
6. download/inspect sequentially or with very low concurrency;
7. stop once fill target is satisfied.

This avoids converting "fill 30 slots" into thousands of API calls.

Cache provider metadata with provider-specific TTLs and immutable remote IDs.

---

# Important non-goals

The following should remain explicitly out of scope for the first multi-source implementation:

- arbitrary web scraping framework;
- anti-bot/CAPTCHA bypass;
- bypassing premium/account download restrictions;
- redistribution of copyrighted mod archives;
- auto-executing installers;
- enabling every accepted mod immediately;
- global optimization across every mod on the internet;
- replacing existing conflict/planner semantics;
- replacing `ArchiveImportService`;
- broad `ManagerDatabase` rewrite;
- broad MainWindow decomposition;
- provider credentials stored in exported recipes.

---

# Recommended first production slice

The best first code slice is **contracts + ranking policy + fake provider**, not a real downloader.

Acceptance criteria:

1. no existing schema change;
2. no network traffic;
3. no WPF change;
4. no Nexus behavior change;
5. normalized provider identity/capability types;
6. a deterministic policy that represents:
   - hard compatibility gates;
   - favorite author priority;
   - preferred tags;
   - source preference;
   - popularity fallback;
7. clear reasons for every included/excluded candidate;
8. unit tests for deterministic ordering and hard-gate precedence;
9. cancellation covered;
10. permanent repository continuity state updated with exact verification.

Then build the first real adapter independently.

---

# Integration notes for active parallel work

At audit time:

- Support Agent 1's deep SQLite atomicity findings are already on canonical main.
- Support Agent 2 has an open documentation-only CI/supply-chain audit PR.
- Support Agent 3 has an open documentation-only MainWindow/UI ownership audit PR.
- Support Agent 4 has an open test-gap/failure/performance audit PR.
- The canonical `PlannerSnapshotRepository` boundary is closed and Windows-verified.
- This audit does not reopen or supersede any of those conclusions.

When integrating this document, preserve concurrent support findings rather than replacing their context.

---

# Successor handoff

The successor implementing any part of this design must:

1. verify actual canonical `main` first;
2. read `AGENTS.md`, `NEXT-AGENT-START-HERE.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and the remaining `README_FIRST.md` order;
3. inspect all support audits merged since this document was written;
4. choose one independently verifiable checkpoint only;
5. preserve archive safety, filesystem recovery, SQLite transaction ownership, planner semantics, cancellation, and exact-verification rules;
6. keep source-provider credentials and quotas isolated by provider;
7. do not claim a remote candidate is non-conflicting until exact file evidence or a trusted manifest supports that claim;
8. update durable handoff/context during the work;
9. commit/push stable checkpoints;
10. explicitly require the next successor to inherit and recursively propagate the same permanent continuity constitution.

**Do not break the chain.**
