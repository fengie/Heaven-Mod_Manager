# State backup, portability, and disaster-recovery audit

Date: 2026-09-27  
Support role: independent support agent  
Canonical base inspected: `fengie/mhw-mods` `main` at `6ada5a5c4cc83afadfba42bc6af6559540920e3d`

## Support prompt used

> Continue from canonical `fengie/mhw-mods` `main`. First inherit the permanent repository continuity rules, inspect current main/history, and read all currently active support PRs so this lane does not duplicate transaction atomicity, CI/supply-chain, MainWindow/WPF ownership, test/performance, async/shutdown, Windows filesystem containment, multi-source acquisition, or MHW semantic coverage work. Then audit the manager's durable user state as a disaster-recovery system: SQLite state, CAS blobs, Mods folders, Mods Archive, save snapshots, last-known-good state, profiles, conflict rules, family preferences, provenance, game registry, credentials, preview cache, recipes, migration backups, support bundles, and game save backups. Determine what can and cannot be restored after (a) a bad deployment, (b) corrupted manager state, (c) lost State directory, (d) lost Mods directory, and (e) moving the manager/game to another machine or root path. Do not modify production source, schema, transaction owners, filesystem deployment logic, WPF, verifier/CI, or the closed PlannerSnapshotRepository boundary. Produce a durable, implementation-oriented audit with a state inventory, authority/derivability classification, portability hazards, restore contracts, backup package design, path-rebasing rules, secrets handling, consistency requirements, test plan, and small independently verifiable implementation checkpoints. Preserve and recursively propagate the repository continuity constitution.

## Why this lane exists

Current parallel support work already covers:

- deep SQLite transaction atomicity;
- CI / verification / supply-chain correctness;
- MainWindow / WPF ownership;
- test gaps, failures, and performance;
- async lifetime / cancellation / shutdown;
- Windows reparse / containment / recovery safety;
- source-neutral remote discovery and bulk acquisition;
- MHW semantic outfit coverage and gap-fill selection.

This audit does **not** repeat those.

It asks a different question:

> If the user's machine, manager state, game path, or mod library changes or is lost, what exactly must be backed up to reconstruct the manager's decisions and content safely?

The repository currently has several features called backup, snapshot, last-known-good, Undo, recipe, migration backup, and support bundle. They serve different purposes and should not be treated as interchangeable.

No production C#, XAML, schema, workflow, verification cache, or persisted handoff file is changed by this support lane.

---

# Executive findings

The manager has strong **local operational recovery** mechanisms, but it does not yet have one complete **disaster-recovery / portable backup contract**.

The most important findings are:

1. **Save snapshots are not full manager backups.**
   `SaveBackupService` captures the game save file, enabled/priority state, game-build hash, and deployment manifest. It does not capture the complete SQLite state, profiles, conflict rules, family preferences, provenance, original-file baselines, trust/issues, operation history, or CAS payload set.

2. **Last-known-good is configuration recovery, not state recovery.**
   `LastKnownGoodService` stores only enabled/priority state plus build hash and an optional snapshot path. The record itself lives in the same SQLite database that a full-state disaster could destroy.

3. **Collection recipe export is not a backup.**
   It exports remote identity/provenance and logical enable/priority/family/category information without payloads. Canonical main currently exposes export but no complete restore/import path. It intentionally cannot reconstruct exact local bytes or all human conflict decisions.

4. **Support bundles are diagnostic artifacts, not restore artifacts.**
   They intentionally export summaries and recent logs rather than a restorable database/CAS state.

5. **The canonical database runs in WAL mode.**
   Therefore copying `manager.db` as an ordinary file while the application is active is not a valid backup strategy. A consistent database snapshot must use SQLite-supported backup/snapshot semantics.

6. **CAS and database must be backed up consistently.**
   The DB can reference blob SHA-256 values in `mod_files`, `original_files`, and `deployment_manifest`. A database snapshot without all required referenced blobs can be structurally valid but operationally unrecoverable.

7. **Absolute source paths are a major portability hazard.**
   `mods.source_path` stores absolute folders. More importantly, `CatalogService` generates local mod IDs from the lowercased **absolute source directory path**. Re-scanning the same copied mod folders under a different tool root creates different IDs and can sever profiles, conflict rules, provenance, family membership, last-known-good state, trust records, issue history, and supersession relationships.

8. **The game registry is outside the per-game SQLite database.**
   `State/games.json` and `State/active-game.txt` are required to reconnect isolated game workspaces. Losing them can make existing `State/Games/<id>/Next` state difficult to rediscover correctly.

9. **Some persisted settings contain absolute derived-cache paths.**
   Nexus visual settings such as `preview:<modId>` and `visuals:<modId>` persist paths into `Next/PreviewCache`. A machine/root move makes these stale unless they are rebased or discarded/rebuilt.

10. **The Nexus API key file must not be silently included in a portable backup.**
    `Next/nexus-api-key.txt` is a credential. A general restore package should exclude secrets by default and explicitly document optional credential export.

11. **The repository lacks one restore-verification pass.**
    There is no end-to-end function that can take a backup, restore into a clean root, rebase paths, validate DB/CAS closure, reconnect the game, and prove that the reconstructed planner state matches the source.

The missing feature is best thought of as a **Recovery Capsule** or **Full Manager Backup**, separate from existing save snapshots and recipes.

---

# Recovery concepts already present

The existing mechanisms are useful and should remain distinct.

## 1. Deployment Undo / journal recovery

Purpose:

- undo a recent committed deployment;
- recover an interrupted deployment transaction;
- restore live filesystem and manager state around deployment boundaries.

Authority:

- `operations`
- `operation_journal`
- `deployment_manifest`
- `original_files`
- CAS blobs
- DeploymentExecutor recovery rules

This is **local transactional recovery**.

It is not a machine-transfer backup.

## 2. Pre-launch save snapshot

`SaveBackupService.CreateAsync` writes a timestamped folder under:

```
Next/Automation/Snapshots/<timestamp>/
```

and may copy the game save file into:

```
save/<save-file-name>
```

It also writes `snapshot.json` containing:

- snapshot ID;
- reason;
- creation time;
- source save path;
- game build SHA-256;
- mod enabled/priority state;
- deployment manifest path -> expected live SHA mapping.

The database receives a `save_snapshots` row with similar metadata.

This is useful for:

- pre-launch safety;
- crash diagnosis context;
- associating a save with a mod configuration.

It is **not** enough to reconstruct the manager.

## 3. Last-known-good

`LastKnownGoodService` persists one JSON setting:

```
automation:last-known-good
```

containing:

- recorded time;
- optional snapshot root;
- game build hash;
- mod ID -> enabled/priority map.

Restore builds a new planner input against current files and refuses a now-blocked setup.

This is intentionally a **configuration rollback**, not byte-for-byte manager recovery.

## 4. Profiles

Profiles persist reusable enabled/priority state in:

- `profiles`
- `profile_mods`
- `profile_rules`

These survive ordinary application restarts because they are in SQLite.

They do not survive loss of the database unless separately backed up.

## 5. Collection recipe

`CollectionRecipeService.ExportAsync` exports:

- mod ID;
- display name;
- enabled;
- priority;
- family ID;
- category;
- source URL;
- Nexus IDs/version lineage;
- supersession state;
- game build data.

It intentionally omits payload redistribution.

On canonical main at this audit base, the UI offers **Export Recipe** only.

A recipe is useful for reconstruction/discovery, but cannot prove or restore exact bytes.

## 6. Legacy migration backup

The v7 -> v8 migration creates a migration backup of legacy state before importing.

That protects the migration boundary.

It is not a recurring current-state backup mechanism for v8.

## 7. Support bundle

`SupportBundleService` exports diagnostic summaries:

- environment;
- DB integrity result;
- recent diagnostics/errors/operations;
- deployment manifest summary;
- conflict rules summary;
- profile summaries;
- incomplete transactions;
- redacted settings;
- table counts;
- recent logs.

It intentionally does not export a restorable database, CAS, source mods, or full state graph.

This is the correct behavior for diagnostics.

---

# Durable-state inventory

A backup design should classify every state item as:

- **authoritative** — loss changes user intent or exact recoverability;
- **reconstructible** — can be recomputed from authoritative sources;
- **ephemeral** — safe to discard;
- **secret** — must be excluded/default-redacted;
- **external** — outside manager state but may need optional backup.

---

# Tool-root / workspace files

## Mods root

For MHW:

```
<ToolRoot>/Mods
```

For generic games:

```
<ToolRoot>/Games/<gameId>/Mods
```

Classification: **authoritative payload source**

Why:

- `mods.source_path` points to these folders;
- `ModScanner` may need source files for re-capture;
- health checks inspect some source files;
- metadata/sidecar inference reads source folders;
- future updates/family inference may depend on local sidecars.

A CAS-only backup can preserve deployable captured bytes but may not preserve the original package structure, metadata sidecars, readmes, optional components not currently captured, or future re-scan behavior.

Therefore a "full portable backup" should either:

1. include the source Mods tree; or
2. explicitly define itself as a **deployment-state-only recovery** with reduced capabilities.

## Mods Archive

Classification: **user payload / optional authoritative history**

Safe cleanup moves duplicate/superseded source folders here.

If the user expects archived versions to remain recoverable, a complete cold backup should include this tree or explicitly exclude it as an optional tier.

## Inbox and Processed

Classification: mostly **workflow staging/history**

Not required to reconstruct deployed manager state.

May contain source archives the user still values.

Recommend optional inclusion.

## Collection Recipes

Stored under:

```
<ToolRoot>/Collection Recipes
```

Classification: **portable metadata artifacts**

They should be included by default in a full user backup because they are small and intentional user exports.

## Support Bundles

Classification: **diagnostic / optional**

Do not include automatically in a recovery package.

They may contain paths and diagnostic metadata.

---

# State root

```
<ToolRoot>/State
```

contains global game registry plus per-game or MHW state.

## games.json

Classification: **authoritative configuration**

Contains GameProfile records including:

- game ID;
- display name;
- absolute GameRoot;
- executable relative path;
- mod-root relative path;
- adapter;
- Steam/store metadata;
- save path and semantic flags.

The absolute GameRoot is not portable, but the logical game identity and adapter metadata are.

A portable backup should preserve the profile but require/recommend **relinking the executable** on restore.

## active-game.txt

Classification: **authoritative preference**

Small but necessary to restore active-game selection.

## manager.db

For MHW:

```
State/Next/manager.db
```

For generic games:

```
State/Games/<gameId>/Next/manager.db
```

Classification: **primary authoritative relational state**

Contains the majority of user and recovery state.

## manager.db-wal / manager.db-shm

Classification: **SQLite runtime state**

Do not manually package these as an ad-hoc backup plan.

The database runs:

```
PRAGMA journal_mode=WAL
PRAGMA synchronous=FULL
```

Use a SQLite-consistent backup API/operation to create the backup database image.

## Blobs

```
Next/Blobs
```

Classification: **authoritative content-addressed recovery payload**

Required referenced blobs can be discovered from DB references.

A full DB backup without referenced blobs is incomplete.

## PreviewCache

```
Next/PreviewCache
```

Classification: **reconstructible derived cache**

Exclude by default to reduce backup size.

On restore:

- clear stale `preview:` and `visuals:` absolute-path settings; or
- rebase and verify files if cache is explicitly included.

Prefer rebuild rather than path rebasing for remote caches.

## Automation/Snapshots

Classification: **optional user recovery history**

Contains game save copies and snapshot metadata.

These should be an optional backup tier because they may be large and may contain personal game saves.

At minimum the newest last-known-good referenced snapshot should be handled coherently if the user expects save rollback.

## Logs

Classification: **diagnostic ephemeral**

Exclude from normal full backup unless user opts in.

## nexus-api-key.txt

Classification: **secret**

Exclude by default.

If future backup UI supports secrets, require explicit opt-in and encrypt/protect them separately.

Never put the API key into a portable plaintext archive by default.

---

# SQLite authority map

The database contains both reconstructible indexes and unique user intent.

## Must preserve for exact user intent

### mods

Preserve IDs, enabled state, priority, source relationship, category, remote identity references.

### mod_provenance

Preserve remote lineage/version evidence.

### mod_supersession

Preserve update relationships.

### family_preferences

Preserve explicit family choices.

### conflict_rules

Critical human decisions:

- explicit incompatibility;
- overlay ordering;
- exact winners;
- saved rules.

A save snapshot that omits these cannot recreate equivalent planner intent.

### resource_providers

Explicit shared-resource provider decisions.

### mod_families / mod_family_members

Persisted family identity and role decisions.

### profiles / profile_mods / profile_rules

User-defined reusable setups.

### original_files

Critical rollback baseline identity.

### deployment_manifest

Current expected live-state contract.

### settings containing user intent

Especially:

- last-known-good;
- other feature preferences/state.

A backup implementation should classify setting keys rather than assume every setting is equally authoritative.

## Important history / intelligence

### game_build_state

Useful to detect build drift after restore.

### mod_revalidation

Preserve known "must revalidate" state.

### adoption_runs / adopted_live_files

Important for provenance/recovery of previously unmanaged content.

### launch_history

Useful for crash diagnosis and trust history.

### mod_trust

Behavioral reliability history.

### mod_issue_suspects

Crash/issue evidence.

### automation_events

History, less critical than core intent but useful.

## Operational recovery

### operations
### operation_journal

If a backup is made only from a **quiescent committed state**, old journals are historical.

If a backup is attempted during a prepared/applying/recovery-required operation, restore semantics become much more complex.

Recommendation:

> Full backup creation should refuse or defer while a mutating transaction is active, unless the backup protocol explicitly captures and proves recovery-required state.

The async/shutdown support lane owns operation coordination; this audit only defines the backup requirement.

## Reconstructible indexes/caches

### mod_files

Can theoretically be rebuilt by rescanning source Mods folders, but exact blob identity is important for reproducibility and may be expensive.

For an exact recovery capsule, keep it via the DB snapshot.

### mod_file_armor

Derived from `mod_files` path semantics.

Can be rebuilt.

### armor_catalog

Bundled adapter data can be reimported.

Can be rebuilt.

### resolver_audit

Historical/explainability data.

Not required for functional reconstruction, but useful.

### diagnostics / error_reports

Diagnostic history.

Optional.

---

# Critical portability hazard: local mod IDs depend on absolute paths

`CatalogService.RefreshFoldersAsync` computes new local mod IDs as:

```
SHA256(lowercase absolute source directory path)
```

truncated into a `local-...` ID.

This is safe for stable identity inside one fixed Mods root.

It is **not portable identity**.

Example:

Old machine:

```
C:\OldRoot\Mods\Cool Armor
```

New machine:

```
D:\ModManager\Mods\Cool Armor
```

If the copied folder is simply rescanned on the new machine, it receives a different ID.

That can break every relation keyed by the old mod ID:

- `mod_files`;
- provenance;
- supersession;
- family membership;
- family preferences;
- conflict rules;
- resource providers;
- profiles;
- revalidation;
- trust;
- issue suspects;
- last-known-good maps;
- launch-history state maps;
- deployment manifest provider references.

Therefore **"copy Mods folder and rebuild DB" is not a safe portability strategy**.

## Recommended restore invariant

A full restore must preserve existing mod IDs.

Do not recreate them from the new absolute path.

Restore should instead:

1. restore database IDs;
2. copy/recreate source folders under the new Mods root;
3. map old source paths to relative source locations;
4. update `mods.source_path` transactionally to the new absolute folders;
5. preserve every existing mod ID and FK relationship;
6. validate uniqueness and folder existence;
7. only then allow ordinary catalog refresh.

Longer-term, new mods should ideally gain a path-independent persisted identity, but changing the global identity scheme is a separate risky migration and should not be bundled into the first backup feature.

---

# Game-profile portability

`GameProfileRegistry` persists absolute GameRoot values in `games.json`.

The app already has a useful relink path for the active game:

- detect missing executable;
- ask the user to locate the executable;
- preserve the GameProfile ID;
- update GameRoot and executable relative path.

This behavior should become part of the formal restore contract.

## Restore rule

Never blindly trust an old absolute GameRoot on another machine.

For each restored game:

1. preserve logical GameProfile ID;
2. preserve adapter/store/Steam metadata;
3. check whether old executable still exists;
4. if not, require relink/discovery;
5. keep the same game ID so `State/Games/<id>` remains attached to the right state.

A restored game should not silently create a new `gameId-2` and strand the old state.

---

# Derived absolute paths inside SQLite

The DB currently stores some paths that are expected to become stale across root moves.

Examples:

## mods.source_path

Authoritative relationship but must be rebased.

## settings preview:/visuals:

Derived cache paths.

Prefer delete/rebuild on restore.

## save_snapshots.root_path

Points into the old state root.

Must be rebased if snapshot history is restored.

## save_snapshots.save_source

Original external game-save path.

Treat as historical hint, not a restore destination.

## migration_runs paths

Historical backup/report paths.

Do not treat as active dependencies.

## adoption_runs.source_root

Historical/source provenance.

May point to old root.

## GameProfile.SavePath

May be machine-specific and require relink/validation.

The backup format needs explicit path semantics instead of blindly serializing absolute paths as if they remain valid.

---

# Path classification for a portable backup

Every persisted path should be classified as one of:

## Tool-relative

Example:

```
Mods/Cool Armor
State/Next/Automation/Snapshots/...
Collection Recipes/...
```

Rebase automatically to the restored ToolRoot.

## Game-relative

Example:

```
nativePC
BepInEx/plugins
Content/Paks
```

Resolve under the relinked GameRoot.

## External-machine path

Examples:

- save file location;
- custom external mod source;
- user-provided external folder.

Do not silently rebase.

Require validation/relink or mark unavailable.

## Derived cache path

Discard/rebuild.

## Secret path

Exclude unless explicit secret backup is selected.

---

# Save snapshots: exact limitations

Current `snapshot.json` captures:

- mod enabled/priority map;
- game build SHA;
- deployment manifest expected hash map;
- optional save copy.

It does **not** capture:

- conflict rules;
- resource-provider pins;
- family preferences;
- profile definitions;
- exact mod file descriptors;
- mod provenance;
- CAS blobs;
- original-file baselines;
- source packages;
- game registry;
- current DB schema/state;
- trust/issue history;
- last-known-good record itself.

Therefore the UI/documentation should continue to call this a **save/mod-state snapshot**, not a complete manager backup.

A future full-backup feature should not repurpose this folder format silently.

Use a versioned separate format.

---

# Snapshot retention consistency

The existing deep SQLite audit already identifies the snapshot-prune DB/payload drift class.

This audit does not duplicate that transaction finding.

From the recovery perspective, the required invariant is:

> Any retained `save_snapshots` row that claims a recoverable payload must either resolve to a present verified snapshot directory or be explicitly marked metadata-only/expired.

Conversely, a retained snapshot directory should have enough self-describing metadata to be useful even if the DB record is lost.

The storage/transaction repair itself belongs to the SQLite support lane.

---

# Last-known-good limitations

The restore operation correctly rebuilds a plan against current files instead of forcing stale bytes.

That is good local safety behavior.

However:

- the last-known-good JSON lives inside the database;
- it refers to mod IDs;
- those IDs are path-derived for local mods;
- the optional SnapshotRoot is an absolute/local path;
- it cannot recover if the DB and state root are lost.

Therefore last-known-good should remain a **runtime rollback feature**.

Do not advertise it as disaster recovery.

---

# Collection recipe limitations

A recipe is deliberately payload-free.

It is a good format for:

- sharing intended mod lists;
- re-discovering remote mods;
- rebuilding approximate logical state.

It is not an exact backup because it lacks:

- local payload bytes;
- captured file hashes for every mod;
- explicit conflict rules;
- resource-provider pins;
- profiles;
- original files;
- current deployment manifest;
- savegame;
- complete game registry;
- all settings.

PR #1 contains broader portable recipe work on a draft branch, but canonical main remains the authority for this audit.

If recipe import later lands, it should stay conceptually separate from full recovery.

---

# Support bundle privacy and recovery boundary

The support bundle is intentionally diagnostic.

It includes:

- manifest paths and hashes;
- conflict rules;
- profile names/counts;
- recent operation/error messages;
- recent logs;
- redacted settings.

This is useful for debugging.

It should **not** become the basis of a restore feature.

A backup format and support bundle have opposite defaults:

## Support bundle

- minimize personal content;
- redact secrets;
- include diagnostics;
- omit mod payloads;
- omit saves.

## Recovery capsule

- preserve exact user state;
- may contain large payloads;
- may include game saves by opt-in/default policy;
- must exclude secrets unless explicitly protected;
- need not include verbose diagnostic logs.

Keep these artifacts separate.

---

# Proposed backup product tiers

A single checkbox called "Backup" will be ambiguous.

Recommend three explicit tiers.

## Tier 1 — Configuration backup

Small.

Includes:

- consistent SQLite snapshot;
- games.json;
- active-game.txt;
- portable path map;
- Collection Recipes.

Excludes:

- CAS blobs;
- Mods payload;
- Mods Archive;
- save snapshots;
- PreviewCache;
- logs;
- secrets.

Guarantee:

> Restores user decisions/history, but may require local/remote mod payload reacquisition before deployment.

## Tier 2 — Exact manager-state backup

Includes Tier 1 plus:

- all DB-referenced CAS blobs required by:
  - mod_files;
  - original_files;
  - deployment_manifest;
- source Mods tree, or a documented equivalent payload package;
- backup manifest with hashes.

Guarantee:

> Can reconstruct the manager's exact captured mod state on another root while preserving mod IDs.

This is the recommended default "Full Backup."

## Tier 3 — Full recovery archive

Includes Tier 2 plus optional:

- Mods Archive;
- save snapshots;
- current game save;
- Inbox/Processed history;
- user-selected extra state.

Still excludes credentials by default.

Guarantee:

> Preserves the broadest user recovery history, not just active manager state.

---

# Proposed Recovery Capsule format

Use a versioned top-level manifest.

Example conceptual structure:

```
recovery-capsule/
  manifest.json
  database/
    manager.db
  registry/
    games.json
    active-game.txt
  blobs/
    <sha256 layout>
  mods/
    <relative mod folders>
  recipes/
    ...
  snapshots/               # optional
  mods-archive/             # optional
```

Do not include live `manager.db-wal` / `manager.db-shm`.

## manifest.json

Should include:

- format version;
- created-at timestamp;
- application version;
- database schema version;
- source ToolRoot as historical metadata;
- source game IDs;
- per-game adapter/store/Steam identity;
- backup tier;
- file inventory with SHA-256 and length;
- required blob SHA set;
- mod ID -> relative source-folder mapping;
- excluded categories;
- whether saves are included;
- whether archives are included;
- whether credentials are intentionally excluded;
- source game-build fingerprint;
- completion marker/hash.

The manifest should be written **last** or have an explicit completed flag so interrupted backup creation cannot be mistaken for valid.

---

# Consistent SQLite backup requirement

Because ManagerDatabase uses WAL mode, do not:

1. copy `manager.db`;
2. ignore `-wal`;
3. call that a backup.

Use a SQLite-supported consistent snapshot method.

Implementation candidates:

- SQLite online backup API through Microsoft.Data.Sqlite capabilities;
- `VACUUM INTO` to a temporary backup database, if semantics/version constraints are acceptable;
- a read transaction plus proper backup API.

The implementation should prove:

- all committed rows at backup boundary are present;
- no partial transaction state is introduced by the backup mechanism;
- integrity_check on the backup DB passes;
- backup DB can be opened without source WAL/SHM.

This is a new read/export boundary, not a reason to alter existing transaction owners.

---

# DB/CAS closure

After creating the database snapshot, compute the exact set of required blob hashes from the snapshot.

At minimum inspect:

- `mod_files.blob_sha256`;
- `original_files.blob_sha256`;
- `deployment_manifest.blob_sha256`;
- any newer tables added before implementation.

For every non-null referenced SHA:

1. locate CAS object;
2. prove it is a regular safe file under BlobRoot;
3. hash it authoritatively;
4. require hash == path/DB SHA;
5. include it in the capsule.

The Windows filesystem audit owns reparse/hardlink containment details.

This audit's invariant is:

> A "complete" exact backup cannot be marked successful while any required referenced blob is missing or hash-invalid.

If user requests a configuration-only tier, missing blobs are allowed because that tier explicitly makes no exact-byte guarantee.

---

# Source Mods tree and CAS are not interchangeable

The CAS preserves captured deployable file contents.

The Mods tree may preserve:

- package sidecars;
- readmes;
- optional components;
- screenshots;
- original archive layout;
- metadata not considered deployable;
- future scanner inputs.

Therefore:

- exact deployment recovery may be possible from DB + CAS;
- full manager behavior recovery may still require source Mods folders.

The product should not silently claim the stronger guarantee.

---

# Restore phases

A safe restore should be staged and fail closed.

## Phase 0 — inspect only

Before writing:

- read capsule manifest;
- validate format version;
- hash/check inventory;
- validate database integrity;
- validate required blobs;
- list games/mods/profiles/rules;
- identify paths requiring relink;
- identify unsupported newer schema.

Show a restore plan.

## Phase 1 — choose destination

Resolve:

- ToolRoot;
- game executable/GameRoot per game;
- optional save destination;
- whether archived/inbox history is restored.

Do not write live game files yet.

## Phase 2 — restore isolated manager state

Restore to temporary/staging state root.

- restore SQLite snapshot;
- restore registry;
- restore CAS;
- restore source Mods tree;
- restore optional history.

No live deployment.

## Phase 3 — rebase paths

Preserve IDs while updating machine-specific paths.

Important:

- `mods.source_path` -> new Mods root + stored relative source;
- `games.json GameRoot` -> relinked root;
- snapshot root paths -> new state root if snapshot history included;
- preview/visual derived settings -> clear/rebuild;
- historical external paths -> retain only as history or mark stale;
- save paths -> validate/relink.

## Phase 4 — validate internal closure

Run:

- DB integrity;
- FK checks;
- source-folder existence;
- required CAS hash checks;
- planner snapshot load;
- conflict-rule graph validation;
- profile mod/rule references;
- game adapter validation;
- health check suitable for isolated state.

## Phase 5 — compare expected state

From the backup manifest store a small deterministic recovery fingerprint.

For example:

- mod count;
- enabled/priority map hash;
- conflict-rule semantic hash;
- profile semantic hash;
- required blob set hash;
- deployment-manifest semantic hash.

After restore/rebase, compare path-independent state.

## Phase 6 — activate

Only after validation:

- atomically promote staged state to active State/Mods paths;
- preserve any existing destination state as rollback backup.

## Phase 7 — live deployment is separate

Do not automatically write restored mods into the game directory as part of state restore.

After restore, let the normal planner/health/deployment flow decide whether to reapply.

This protects against:

- game-version changes;
- moved game root;
- different external/manual files;
- stale compatibility assumptions.

---

# Restore should preserve IDs

This deserves its own hard invariant.

## Never do this on restore

- copy Mods folders;
- delete DB;
- run fresh catalog discovery;
- accept newly generated local IDs;
- try to match by display name later.

That destroys relational identity.

## Do this instead

- restore DB first in isolation;
- copy Mods folders according to backup manifest;
- update source paths for existing IDs;
- validate;
- start ordinary catalog refresh only after the mapping is correct.

If a source folder is intentionally omitted, keep the mod ID and mark source unavailable/reacquisition required rather than inventing a replacement ID.

---

# Proposed portable source mapping

The capsule manifest should store:

```
modId
sourceKind
relativeSourcePath
originalSourcePath
sourcePayloadIncluded
sourceArchiveSha256?
```

For mods inside manager ModsRoot:

```
sourceKind = ManagedModsRoot
relativeSourcePath = Cool Armor
```

For external/manual source roots:

```
sourceKind = External
originalSourcePath = X:\Somewhere\...
```

Do not pretend external paths can be rebased automatically.

---

# New-machine restore behavior

Expected user flow:

1. install/run manager;
2. choose "Restore recovery capsule";
3. select capsule;
4. manager validates capsule;
5. manager lists games from backup;
6. user locates each missing executable or accepts auto-discovery;
7. manager restores state under new ToolRoot;
8. manager rebases managed source paths while preserving IDs;
9. derived caches are rebuilt;
10. health/planner validation runs;
11. user chooses whether to deploy restored configuration.

No manual editing of JSON/SQLite should be required.

---

# Lost-State scenarios

## Scenario A — bad deployment but State intact

Use existing:

- DeploymentExecutor recovery;
- Undo;
- last-known-good;
- save snapshot.

No full capsule restore required.

## Scenario B — manager.db corrupted, Mods and Blobs intact

Current situation:

- source mods still exist;
- captured blobs may still exist;
- user decisions in DB may be lost.

Without a full backup, catalog can rediscover folders but cannot reconstruct all exact:

- profiles;
- conflict rules;
- resource provider pins;
- family preferences;
- trust/issues;
- provenance;
- original baselines.

A configuration or full recovery capsule solves this.

## Scenario C — State directory lost, Mods intact

Even worse:

- DB gone;
- CAS gone;
- games.json/active marker gone;
- last-known-good gone;
- snapshots gone;
- profiles/rules/provenance gone.

Fresh catalog rebuild also generates mod IDs from current source paths.

A prior capsule is required for exact state recovery.

## Scenario D — Mods directory lost, State + Blobs intact

Some exact deployment data may still be recoverable from CAS.

But:

- source re-scan fails;
- sidecars/readmes/options are gone;
- health may report missing source;
- updates/metadata inference can degrade.

Full backup should include source Mods tree if it claims full behavior recovery.

## Scenario E — entire old tool root lost, capsule available

This is the target full restore case.

Must:

- recreate logical game state;
- relink game install;
- preserve IDs;
- rebase paths;
- restore CAS/source payloads;
- validate before deployment.

## Scenario F — game moved, manager state intact

The current active-game relink flow is already a useful foundation.

Full recovery should reuse that model, not invent direct path rewriting in the backup layer.

---

# Game save handling

Game saves are different from manager state.

Current `SaveBackupService` can find MHW save via:

- GameProfile.SavePath;
- `MOD_MANAGER_SAVE_PATH`;
- `MHW_SAVE_PATH`;
- Steam userdata scanning.

A full backup may offer:

- exclude saves;
- include latest save;
- include retained snapshot history.

Do not overwrite a current game save automatically during manager-state restore.

Restoring a save should be a separate explicit action with:

- source snapshot timestamp;
- source game/build metadata;
- destination preview;
- backup of current destination save before replacement.

---

# Secrets

Current known credential path:

```
Next/nexus-api-key.txt
```

Environment-provided `NEXUS_API_KEY` is not manager-owned durable state.

Rules:

1. exclude credential files from normal capsule;
2. manifest states `credentialsIncluded=false`;
3. restore does not require Nexus credentials to reconstruct local state;
4. user reconnects credentials afterward;
5. if encrypted secret export is added later, make it an independent opt-in feature.

Do not put API keys into Collection Recipes, support bundles, or ordinary backup JSON.

---

# Preview and visual cache handling

Nexus preview settings persist absolute file paths.

The cache is reconstructible.

Recommended restore behavior:

- do not include PreviewCache in standard capsule;
- remove/rebuild `preview:` and `visuals:` settings that point to non-existent old roots;
- preserve remote provenance so metadata refresh can reacquire artwork.

Do not fail an otherwise valid restore because thumbnail files are absent.

---

# Backup creation concurrency

A backup should represent a coherent logical point.

It should not race with:

- deployment;
- Undo/recovery;
- profile/rule mutations;
- metadata refresh that writes provenance/family/supersession state;
- Smart Inbox/import;
- duplicate cleanup;
- game-registry changes.

The async/lifetime support lane recommends a shell operation coordinator.

The backup feature should consume that coordination once available.

Until then, the first implementation should be scoped to an operation state where no mutating workflow is active.

Do not solve concurrency by changing every transaction owner.

---

# Backup atomicity

Use a temporary capsule location:

```
<destination>/.backup-<guid>.partial
```

Then:

1. create DB snapshot;
2. hash/collect required blobs;
3. copy selected payloads;
4. write manifest;
5. verify all inventory hashes;
6. write completion marker;
7. atomic rename to final backup name.

Cancellation/failure:

- delete or leave clearly marked `.partial`;
- never expose it as a valid completed backup.

For zip packaging:

- build verified directory first;
- then create archive;
- hash archive;
- only then present success.

---

# Restore atomicity

Never restore directly over active state file-by-file.

Use:

```
restore-staging-<guid>
```

Validate fully.

Then preserve current destination as:

```
pre-restore-<timestamp>
```

and atomically/safely promote staged directories at a well-defined boundary.

Windows filesystem containment requirements from PR #7 still apply.

---

# Backup manifest verification

At restore, verify:

- all listed files exist;
- length matches;
- SHA-256 matches;
- no unexpected path traversal;
- no absolute archive paths;
- no duplicate case-insensitive destination paths;
- no reserved device names;
- supported format/schema versions;
- database integrity passes;
- required blob closure passes.

The archive-safety implementation can reuse existing path-safety concepts.

Do not trust a backup merely because it was created by this application.

Backups may be copied, corrupted, or tampered with.

---

# Test gaps found in this lane

The current automation test for save snapshots asserts roughly:

- snapshot succeeds;
- one save file was copied;
- `snapshot.json` exists.

That is useful smoke coverage but does not validate recovery completeness.

No canonical-main test found in this lane proves:

- full DB restore;
- DB + CAS closure;
- new-root path rebasing;
- mod-ID preservation across ToolRoot changes;
- game-profile relink during restore;
- preview-path rebuild;
- corrupted backup detection;
- missing blob rejection;
- configuration-only degraded restore;
- secret exclusion;
- exact state equivalence after restore.

These should be new tests for the future recovery feature, not changes to existing snapshot semantics.

---

# Required regression: path-derived mod identity

Create a deterministic test:

1. create Mods root A;
2. catalog folder `Cool Armor`;
3. record generated mod ID;
4. create profile/rule/family references to that ID;
5. build recovery capsule with relative source mapping;
6. restore under Mods root B;
7. assert:
   - same mod ID;
   - source_path points into B;
   - profile/rule/family references still point to same ID;
   - no new duplicate mod ID was generated by catalog refresh.

This is the most important portability regression.

---

# Required regression: naïve rescan would break identity

A documentation/design test fixture should also prove the underlying risk:

```
Hash("C:\A\Mods\Cool Armor") != Hash("D:\B\Mods\Cool Armor")
```

The production restore must therefore not rely on fresh ID generation.

---

# Required regression: WAL-safe database snapshot

Test:

1. initialize DB in WAL mode;
2. keep connection open;
3. perform committed writes;
4. create backup using proposed API;
5. open backup independently without source WAL/SHM;
6. run integrity_check;
7. assert latest committed rows present;
8. assert uncommitted rows absent.

Do not test an unsupported raw-copy strategy as the production mechanism.

---

# Required regression: CAS closure

Seed:

- mod_files reference A;
- original_files reference B;
- deployment_manifest reference C.

Then:

- backup succeeds only when A/B/C valid;
- missing B causes exact-backup failure;
- wrong bytes under hash-named C cause failure;
- configuration-only backup may succeed with explicit degraded guarantee.

Filesystem object-type/reparse adversarial cases belong with PR #7's containment tests.

---

# Required regression: portable derived cache

Seed:

```
preview:<id> = C:\OldRoot\State\Next\PreviewCache\...
visuals:<id> = [...]
```

Restore to:

```
D:\NewRoot
```

Expected:

- core restore succeeds;
- stale preview paths are cleared/rebuilt or correctly rebased if cache included;
- thumbnail cache is never treated as authoritative recovery failure.

---

# Required regression: game registry

Backup game ID `monster-hunter-world` pointing at old root.

Restore where old root is missing.

Expected:

- same logical game ID;
- restore enters relink-required state;
- selecting new executable updates GameRoot;
- per-game state remains connected;
- no duplicate new game profile is created.

---

# Required regression: secret exclusion

Place:

```
Next/nexus-api-key.txt
```

and secret-looking settings.

Default backup must:

- not contain key bytes;
- state secrets excluded in manifest;
- restore works without credential.

If future encrypted credential export exists, test separately.

---

# Required regression: backup corruption

Mutate one byte in:

- database snapshot;
- required blob;
- Mods payload;
- manifest.

Restore inspection must fail before promotion.

---

# Required regression: interrupted backup

Inject cancellation/failure after:

- DB snapshot;
- some blobs copied;
- source Mods copied;
- manifest temp write.

No partial artifact may appear as completed.

---

# Required regression: interrupted restore

Inject failure during staging/rebase/validation.

Expected:

- original active manager state remains intact;
- staged restore remains quarantined or is cleaned;
- no live game deployment occurs.

---

# Suggested recovery fingerprint

To prove semantic equivalence without comparing machine-specific paths, generate a path-independent digest from sorted normalized state.

Candidate domains:

- `modId -> enabled/priority/category/family`;
- conflict-rule semantic fields;
- resource-provider map;
- profile memberships/rules;
- family membership/preferences;
- provenance remote IDs/version lineage;
- required blob hash set;
- deployment manifest relative path/provider/blob hash;
- original baseline map.

Do not include:

- absolute source roots;
- cache paths;
- timestamps that naturally change;
- diagnostics;
- log paths.

Use this fingerprint only as verification evidence, not as a replacement for full validation.

---

# Recommended implementation checkpoints

## Checkpoint 1 — recovery state inventory + pure portable path model

No DB writes.
No schema.
No filesystem mutation.
No WPF.
No network.

Add pure types/functions that classify persisted paths:

- managed-tool-relative;
- game-relative;
- external;
- derived;
- secret.

Add tests.

Also expose a pure calculation of required DB-referenced CAS SHA set.

This is the safest first source checkpoint.

## Checkpoint 2 — consistent configuration backup exporter

Add a service that creates:

- SQLite-consistent backup DB;
- games.json;
- active-game.txt;
- manifest;
- recipes.

Exclude blobs/payload/secrets.

Verify backup DB integrity.

No restore yet.

## Checkpoint 3 — exact CAS closure

Add required blob collection/verification and include CAS payloads.

Do not add source Mods tree until closure tests are green.

## Checkpoint 4 — source Mods payload + ID/source rebasing model

Add backup manifest mapping:

```
mod ID -> relative source folder
```

Implement restore into an **isolated temporary root**.

Prove IDs survive root change.

Still no live state promotion.

## Checkpoint 5 — game registry relink + restore validation

Integrate GameProfile relink.

Validate planner/profile/rule/CAS/source closure in staging.

## Checkpoint 6 — atomic restore promotion

Only after staging tests exist.

Preserve pre-restore state for rollback.

Do not auto-deploy live files.

## Checkpoint 7 — optional save/snapshot/archive tiers

Add game-save and historical payload choices after core state recovery is proven.

## Checkpoint 8 — WPF backup/restore UI

Only after backend backup and isolated restore are independently verified.

---

# Recommended first production boundary

If this support audit becomes implementation work, the first source checkpoint should be:

> **Pure recovery inventory and portable-path/CAS-reference model only.**

Acceptance criteria:

1. no schema change;
2. no WPF change;
3. no existing transaction owner change;
4. no live deployment behavior change;
5. no network;
6. no secrets read/exported;
7. deterministic classification of manager-owned paths;
8. deterministic extraction of required CAS references;
9. tests for source-root rebasing semantics;
10. explicit proof that local mod IDs must be preserved rather than regenerated;
11. continuity/handoff updated;
12. fresh exact verification for any production-source change.

---

# Non-goals

Do not combine the first recovery feature with:

- generic cloud sync;
- automatic OneDrive/Dropbox integration;
- source-provider downloads;
- Nexus auth redesign;
- encryption/key management;
- ManagerDatabase decomposition;
- filesystem reparse hardening;
- DeploymentExecutor redesign;
- WPF page splitting;
- FOMOD implementation;
- semantic outfit coverage;
- profile feature expansion;
- full deduplication redesign.

Those are separate boundaries.

---

# Relationship to other support audits

## Deep SQLite transaction audit

Authority for:

- transaction ownership;
- DB/filesystem atomicity gaps;
- snapshot prune DB/payload drift;
- write sequencing.

This audit adds recovery-product requirements but does not supersede it.

## Windows filesystem safety audit

Authority for:

- reparse/junction physical containment;
- hardlink/CAS object trust;
- ReplaceFileW partial mutation;
- recursive traversal;
- Windows path/device-name edge cases.

Recovery capsule copy/extract must obey that audit.

## Async lifetime audit

Authority for:

- foreground/background operation exclusion;
- graceful shutdown/join;
- mutation coordination.

Backup creation should eventually use the coordinator boundary it recommends.

## CI/supply-chain audit

Authority for:

- what verification evidence means;
- cache/promotion integrity;
- workflow provenance.

A recovery feature still needs the normal exact gate.

## Multi-source acquisition audit

A configuration-only restore may later reacquire remote payloads through provider adapters.

This audit does not design that download workflow.

## MHW semantic coverage audit

No direct overlap.

Coverage data may be reconstructed after restore from mod files/catalog.

---

# Durable conclusions

1. Existing save snapshots are **not** full manager backups.
2. Last-known-good is **not** disaster recovery.
3. Collection Recipes are **not** exact backups.
4. Support Bundles are **not** restore artifacts.
5. A valid live DB backup must be SQLite/WAL-consistent.
6. Exact recovery requires DB + all required referenced CAS blobs.
7. Full behavior recovery should also preserve source Mods folders.
8. Local mod IDs currently depend on absolute source folder paths, making naïve cross-root rescans identity-breaking.
9. Restore must preserve mod IDs and transactionally rebase `mods.source_path`.
10. `games.json` and `active-game.txt` are essential state outside the per-game DB.
11. GameRoot should be relinked, not blindly restored.
12. Derived preview/cache paths should be rebuilt or explicitly rebased.
13. Credentials must be excluded from portable backups by default.
14. Restore must stage/validate before activating manager state.
15. Restoring manager state must not automatically deploy to the live game.
16. A complete backup should be self-verifying with hashes and a versioned manifest.
17. The first implementation should be a pure recovery/path/reference model, not a giant backup UI.

---

# Verification honesty

Performed:

- inspected canonical `main` and recent history;
- inspected all active support/product PR scopes to avoid duplication;
- inspected `SaveBackupService`;
- inspected `LastKnownGoodService`;
- inspected `AutomationCoordinator`;
- inspected `CollectionRecipeService`;
- inspected `SupportBundleService`;
- inspected `GameProfileRegistry`;
- inspected `AppPaths` startup state layout;
- inspected `ManagerDatabase` WAL configuration;
- inspected canonical schema;
- inspected `CatalogService` local ID generation;
- inspected Nexus persisted preview/credential behavior;
- inspected existing backup smoke test;
- inspected migration documentation.

Not performed:

- no production source changed;
- no schema changed;
- no filesystem/deployment behavior changed;
- no test suite run;
- no Windows gate run;
- no verification cache promotion;
- no actual corruption/recovery experiment executed.

All failure claims in this document are static design/control-flow conclusions unless explicitly stated otherwise.

---

# Successor handoff

A successor implementing any part of this audit must first:

1. verify actual canonical `main`;
2. inspect `git status` in its real workspace and preserve unexplained work;
3. read `AGENTS.md`;
4. read `NEXT-AGENT-START-HERE.md`;
5. read `_AGENT_CONTEXT/CURRENT_REVISION.json`;
6. read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`;
7. read active `_AGENT_CONTEXT/LEARNED_RULES.md`;
8. follow the remaining `_AGENT_CONTEXT/README_FIRST.md` order;
9. read every support audit merged since this document was created;
10. keep backup semantics distinct from save snapshots, recipes, support bundles, and deployment Undo;
11. preserve mod IDs across root changes;
12. never call a raw live `manager.db` file copy a valid WAL-safe backup;
13. never call DB-only state an exact backup if required CAS blobs are absent;
14. never silently include credentials;
15. never auto-deploy live game files as part of state restore;
16. implement one independently verifiable checkpoint only;
17. update durable context during work;
18. commit/push meaningful checkpoints;
19. run the relevant handoff/verification checks;
20. explicitly require the next successor to inherit, preserve, and recursively propagate the permanent continuity constitution to the agent after them.

**Do not break the chain.**
