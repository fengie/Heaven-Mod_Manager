# Mod lifecycle and referential-integrity audit — 2026-09-27

## Status

**Documentation-only independent support audit. No production behavior changed.**

Canonical `main` inspected at task lock:

`a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1`

That commit already contains the integrated parallel support audits and active Learned Rules LR-001 through LR-006.

This audit is deliberately separate from:

- `SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`, which owns atomicity/crash windows such as DuplicateCleanup move-before-delete;
- `WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`, which owns physical containment/reparse/CAS integrity;
- `STATE_BACKUP_PORTABILITY_RECOVERY_AUDIT.md`, which owns cross-root identity preservation and recovery capsules;
- `LEGACY_MIGRATION_RECOVERY_AUDIT.md`, which owns restartable v7 migration convergence;
- `ASYNC_LIFETIME_CANCELLATION_AUDIT.md`, which owns background mutation/lifetime coordination.

This document owns a different invariant:

> **When a mod entity is retired successfully, all live semantic state that can affect a future mod with that identity must be retired or deliberately versioned too.**

---

# 1. Why this lane was selected

Parallel work already covers the major active boundaries: filesystem containment, native replacement, CI/supply-chain, MainWindow ownership, async lifetime, migration retry, backup/portability, network trust, diagnostics privacy, Smart Pack/provider architecture, semantic coverage, broad test gaps, and launch-health revalidation.

The remaining deletion/re-import lifecycle was not covered by a specialized audit.

The key source combination is:

1. `DuplicateCleanupService.ArchiveSafeAsync` performs a real durable mod deletion with:
   - filesystem archive move;
   - `DELETE FROM mods WHERE id=$m`.
2. `Schema.cs` has strong foreign-key ownership for many mod-owned tables, but several live resolver/configuration tables intentionally have no FK to `mods`.
3. `CatalogService.RefreshFoldersAsync` derives local mod IDs from the lowercased **absolute source directory path**.
4. `PlannerSnapshotRepository.LoadAsync` loads all persisted conflict rules and resource-provider pins into every planner snapshot.
5. `ConflictEngine` applies those rules again whenever their referenced IDs appear among current candidates.
6. mod-keyed settings such as `preview:<id>`, `visuals:<id>`, `update:<id>`, and `visual-public-last:<id>` are not relationally owned by the mod row.

That creates an identity-reuse hazard after an otherwise successful delete.

---

# 2. Internal assignment

## Exact scope

Inspect and classify:

- all durable references to `mods.id`;
- which references are protected by declared foreign keys;
- which references are live resolver/configuration state;
- which references are intentionally historical;
- the successful mod deletion path;
- local mod identity generation;
- planner consumption of stale rules/providers;
- mod-keyed settings behavior;
- health/integrity checks;
- existing tests around deletion, duplicate cleanup, rules, and planner behavior.

## Questions

1. Does successful mod deletion retire every live rule/configuration reference to the deleted identity?
2. Can a later package receive the same local mod ID?
3. If so, can old resolver choices silently become active again?
4. Which state should cascade, which should be explicitly retired, and which should remain historical?
5. Does current health checking detect relational or semantic orphans?
6. What exact regression contract should precede any production/schema change?

## Exclusions

This audit does **not**:

- redesign the DuplicateCleanup filesystem transaction/crash protocol;
- add foreign keys indiscriminately;
- delete historical audit/launch/save data;
- change local mod ID format;
- redesign backup portability;
- change planner conflict semantics;
- modify production source, schema, tests, verification cache, or live filesystem behavior.

---

# 3. Executive result

## P1 — confirmed live resolver state survives successful mod deletion

`DuplicateCleanupService.ArchiveSafeAsync` eventually executes:

`DELETE FROM mods WHERE id=$m`

Foreign-key cascades clean many child rows correctly. However, these planner-active references have no FK to `mods`:

- `conflict_rules.left_mod_id`
- `conflict_rules.right_mod_id`
- `conflict_rules.winner_mod_id`
- `resource_providers.mod_id`

They therefore survive a successful mod-row deletion.

While the mod ID is absent, many stale rules are inert because current conflict candidates do not contain that ID.

They are **not retired**, however.

## P1 — confirmed identity reuse can reactivate those old decisions

`CatalogService.RefreshFoldersAsync` creates a local ID from the SHA-256 of the lowercased absolute source directory path.

Therefore:

- old package at `<ModsRoot>\A` -> deterministic local ID X;
- package X is archived/deleted from the DB;
- a later package is imported or restored at the same `<ModsRoot>\A` path;
- catalog refresh can create ID X again.

The new package need not be byte-identical to the old package.

Once X exists again:

- an old exact-path winner whose `winner_mod_id=X` can apply again;
- an old mod-pair overlay involving X can apply again;
- an old incompatibility involving X can block again;
- an old `resource_providers.mod_id=X` pin can select X again when the namespace collides.

This is not only orphan-row clutter. It is **semantic reactivation across entity incarnations**.

A user's old decision about package X can silently become a decision about different bytes that happen to reuse X's path-derived identity.

---

# 4. Current deletion ownership: what already works

The schema already gives strong automatic cleanup for many true child records.

Deleting `mods.id` cascades or nulls these relationships:

| State | Relationship | Current delete behavior |
|---|---|---|
| `mod_provenance.mod_id` | FK -> mods | CASCADE |
| `mod_supersession.older_mod_id` | FK -> mods | CASCADE |
| `mod_supersession.newer_mod_id` | FK -> mods | CASCADE |
| `family_preferences.selected_mod_id` | FK -> mods | SET NULL |
| `mod_revalidation.mod_id` | FK -> mods | CASCADE |
| `adoption_runs.created_mod_id` | FK -> mods | SET NULL |
| `mod_files.mod_id` | FK -> mods | CASCADE |
| `mod_file_armor` | composite FK -> mod_files | CASCADE |
| `mod_family_members.mod_id` | FK -> mods | CASCADE |
| `profile_mods.mod_id` | FK -> mods | CASCADE |
| `mod_trust.mod_id` | FK -> mods | CASCADE |
| `mod_issue_suspects.mod_id` | FK -> mods | CASCADE |

These are strengths. A future fix should use them rather than replacing them with broad handwritten cleanup.

`profile_rules.rule_id` also has `ON DELETE CASCADE` to `conflict_rules`, so retiring a conflict rule through a proper lifecycle boundary can clean its profile reference automatically.

---

# 5. Live references that are not owned by the mod FK

## 5.1 conflict_rules

Schema:

- `left_mod_id TEXT NULL`
- `right_mod_id TEXT NULL`
- `winner_mod_id TEXT NULL`

None references `mods(id)`.

These rows are planner input, not historical-only data.

`PlannerSnapshotRepository.LoadAsync` loads every conflict rule.

For `ExactWinner`, it also creates the path -> winner lookup in `ExactWinners`.

`ConflictRuleIndex` builds live overlay/incompatibility indexes from mod IDs without checking that each ID currently exists in `mods`.

`ConflictEngine` appropriately requires a rule's IDs to match current candidates before applying it. That keeps a deleted ID dormant, but it is exactly why deterministic ID reuse can resurrect the rule later.

### Current creation evidence

Conflict rules are not theoretical legacy leftovers:

- legacy migration imports exact winners and pair relations;
- `ManagerDatabase.ChainManualFamilyAsync` writes explicit mod-pair overlay rules for user-authored families.

Therefore deletion lifecycle needs an explicit policy for them.

## 5.2 resource_providers

Schema:

`resource_providers(namespace PRIMARY KEY, mod_id TEXT NOT NULL)`

There is no FK to `mods`.

Legacy migration imports these pins.

`PlannerSnapshotRepository` loads every row into `ResourceProviders`.

`ConflictEngine` uses a matching resource-provider pin as an explicit winner when the pinned mod is among current candidates.

A deleted ID is dormant, not retired.

## 5.3 mod-keyed settings

`settings` is a generic key/value table, so relational ownership cannot be inferred automatically.

Confirmed current per-mod keys include:

- `preview:<modId>`
- `visuals:<modId>`
- `update:<modId>`
- `visual-public-last:<modId>`

Effects after identity reuse include:

- old preview/gallery paths can attach to the new package while cached files remain;
- an old update timestamp can immediately show an update badge for the new package;
- an old public-visual throttle timestamp can delay fresh preview discovery.

These are lower severity than resolver rules but are the same lifecycle-class bug.

---

# 6. References that should not be blindly cascaded

A correct fix is **not** “make every mod-looking column an FK with ON DELETE CASCADE.”

Some records are historical evidence.

## resolver_audit.winner_mod_id

This is an audit record of a decision at a point in time.

Deleting the current mod should not silently rewrite history.

If future UI needs to render a display name, it should tolerate a historical ID whose current entity no longer exists.

## launch_history.state_json

Historical launch state can contain mod IDs.

It is intentionally a snapshot.

## save_snapshots.mod_state_json

Also historical/snapshot state.

A deleted current mod does not imply old save-snapshot evidence should be rewritten.

## automation_events / diagnostic history

Historical event payloads may mention mod IDs or paths and should generally be retained according to their own retention policy.

### Principle

Classify references into:

1. **live semantic/configuration state** — must be retired or explicitly preserved by policy;
2. **current deployed state** — must be guarded by a stronger precondition/transition;
3. **historical evidence** — normally preserved even when the live entity disappears.

Deletion correctness depends on that classification, not merely FK syntax.

---

# 7. Deployment manifest needs a deliberate contract

`deployment_manifest.provider_mod_id` has no FK to `mods`.

That can be reasonable: a manifest records current live ownership and participates in deployment/rollback semantics.

However, a mod retirement path must not casually delete a mod that is still the current deployed provider without defining what happens to live managed files.

Current DuplicateCleanup chooses disabled duplicate/superseded candidates, which makes a current-manifest reference less likely, but the invariant is not encoded in the schema or a centralized mod-retirement API.

Recommended contract:

> Before current mod state is retired, prove the mod is not an active provider in `deployment_manifest`, or run the established deployment transition that removes/replaces its live ownership first.

Do not solve this by adding `ON DELETE CASCADE` to `deployment_manifest.provider_mod_id`; that could erase recovery-relevant live state without mutating the filesystem.

---

# 8. Family-state lifecycle observations

`mod_family_members.mod_id` correctly cascades on mod deletion.

But:

- `mods.family_id` is a text value, not an FK to `mod_families`;
- `mod_families` can remain after its last member disappears;
- remaining family members may still retain the family ID after another member is removed.

That may be acceptable when a family survives loss of one component.

The contract is currently implicit.

Do not classify this as a confirmed defect without deciding:

- whether empty families should be garbage-collected;
- whether a family with one remaining member should remain persisted;
- what happens when the deleted member was the `Main` role.

Add characterization tests before changing it.

---

# 9. Catalog refresh and manually missing source folders

`CatalogService.RefreshFoldersAsync` is add-only:

- it reads existing DB mods by source path;
- it enumerates present source directories;
- it upserts only directories not already known.

It does not delete/tombstone a DB mod merely because its source folder disappeared.

That behavior is not automatically wrong.

The manager intentionally has captured CAS state and can preserve exact deployed bytes even if source folders later change. README safety guidance also tells users not to delete `Mods` casually.

Therefore this audit does **not** recommend making ordinary catalog refresh delete missing mods.

Instead:

- explicit manager-owned retirement must have complete lifecycle semantics;
- missing-source health should remain visible/fail-safe where recapture is required;
- a future “forget/remove mod” command should call the explicit retirement boundary rather than treating filesystem absence as deletion authority.

---

# 10. Health-check gap

`HealthService.ScanAsync` currently calls:

`ManagerDatabase.IntegrityCheckAsync`

which runs:

`PRAGMA integrity_check`

SQLite documents that `integrity_check` does **not** report foreign-key errors; `PRAGMA foreign_key_check` is the separate check for violated declared foreign keys.

Primary reference:

https://www.sqlite.org/pragma.html#pragma_integrity_check

and:

https://www.sqlite.org/pragma.html#pragma_foreign_key_check

This creates two distinct observability gaps:

1. current Health does not report violations of **declared** foreign keys;
2. even `foreign_key_check` cannot detect `conflict_rules` or `resource_providers` orphans because those relationships are not declared as FKs.

A robust health pass therefore needs both:

- SQLite FK checking;
- semantic orphan queries for live non-FK references.

---

# 11. Existing tests and missing coverage

## Existing useful coverage

Current tests exercise:

- duplicate fingerprint detection;
- planner/Explain Why with explicit conflict rules;
- manual-family conflict-rule replacement/order behavior;
- many deployment/recovery safety paths.

These prove the underlying components work, but not entity retirement closure.

## Missing focused lifecycle coverage

No checked test asserts that a successful mod deletion retires:

- exact-winner rules;
- mod-pair overlay rules;
- incompatibility rules;
- resource-provider pins;
- per-mod settings.

No checked test performs:

> delete mod -> recreate different package at same deterministic source path -> refresh catalog -> plan again

and asserts that the old package's semantic choices do not reappear.

No checked health test asserts:

- `PRAGMA foreign_key_check` is clean;
- semantic non-FK live references point to current entities.

---

# 12. Exact regression backlog

## A. Identity reuse characterization

### `Catalog_same_source_path_reuses_local_mod_id`

1. create Mods root;
2. create source folder A;
3. call `CatalogService.RefreshFoldersAsync`;
4. record mod ID X;
5. remove/retire row and source folder;
6. recreate a different package at the same absolute A path;
7. refresh;
8. assert ID == X under current identity design.

This pins the precondition for all resurrection tests.

It is not itself a failure; it documents current identity semantics.

## B. Exact-winner resurrection

### `Retired_mod_exact_winner_does_not_apply_to_new_incarnation_with_same_local_id`

Fixture:

- mod A and mod B share a path;
- explicit exact rule chooses A;
- A is retired successfully;
- recreate different package at A's same absolute source path;
- catalog creates the same local ID;
- capture new A bytes;
- enable A/B and build planner.

Expected:

- old exact rule has been retired;
- planner does not identify the new A package as an explicit winner because of the old package's rule.

## C. Mod-pair overlay resurrection

### `Retired_mod_overlay_rule_does_not_apply_to_new_incarnation`

Same delete/recreate sequence.

Expected:

- no old explicit overlay edge remains involving X.

## D. Incompatibility resurrection

### `Retired_mod_incompatibility_does_not_block_new_incarnation`

Expected:

- a different package that reuses X is not automatically declared incompatible with the old rule's peer.

## E. Resource-provider resurrection

### `Retired_mod_resource_provider_pin_does_not_select_new_incarnation`

Use a managed path with a known resource namespace.

Expected:

- old namespace pin referencing X is removed or otherwise retired before X can be reused.

## F. Mod-scoped settings cleanup

### `Retired_mod_clears_live_mod_scoped_settings`

Seed:

- `preview:X`
- `visuals:X`
- `update:X`
- `visual-public-last:X`

After retirement:

- none remains.

Then recreate X and verify no stale update badge/gallery/throttle state is inherited.

## G. FK-owned state closure

### `Retired_mod_cascades_declared_owned_rows`

Seed the supported FK-owned child state and retire the mod.

Assert no current child row remains in:

- mod_provenance;
- mod_supersession involving X;
- mod_revalidation;
- mod_files / mod_file_armor;
- mod_family_members;
- profile_mods;
- mod_trust;
- mod_issue_suspects.

Also assert:

- family_preferences selected X becomes NULL;
- adoption_runs created_mod_id X becomes NULL.

This guards schema behavior through future migrations.

## H. Historical evidence preservation

### `Retired_mod_preserves_historical_audit_records`

Seed resolver/launch/timeline history referring to X.

Expected:

- historical evidence remains queryable;
- current UI/read paths tolerate unresolved historical IDs.

This prevents an over-broad cleanup implementation.

## I. Deployment-manifest precondition

### `Retire_mod_refuses_when_current_manifest_still_uses_mod_as_provider`

Expected:

- retirement fails before deleting current mod state;
- manifest and live files remain unchanged;
- caller must first use normal deployment transition.

If product policy instead introduces a combined undeploy+retire workflow, that must be a separately journaled/tested boundary.

## J. Foreign-key health

### `Health_reports_declared_foreign_key_violation`

Test fixture can deliberately create a violation using a raw connection with foreign-key enforcement disabled, then restore normal connection behavior.

Expected health issue identifies the FK violation.

Do not weaken production `PRAGMA foreign_keys=ON`.

## K. Semantic orphan health

### `Health_reports_live_rule_or_resource_provider_with_missing_mod`

Seed orphan conflict/resource records by raw SQL.

Expected health output distinguishes:

- stale rule endpoint/winner;
- stale resource-provider pin.

---

# 13. Recommended first production checkpoint

Do **not** start with a schema migration that adds FKs everywhere.

The smallest independently verifiable boundary is:

## Centralized mod-retirement semantics + tests

Introduce one explicit mod-retirement database boundary, for example:

`ManagerDatabase.RetireModAsync`

or a narrowly named repository/service if architecture ownership warrants it.

Within **one DB transaction**, after caller-level filesystem/live-state preconditions are satisfied:

1. assert the mod exists;
2. assert it is not an active `deployment_manifest.provider_mod_id` unless an explicitly defined safe transition already handled that state;
3. delete live `conflict_rules` where:
   - `left_mod_id = X`;
   - `right_mod_id = X`;
   - `winner_mod_id = X`;
4. delete `resource_providers` where `mod_id = X`;
5. delete known live per-mod settings:
   - `preview:X`;
   - `visuals:X`;
   - `update:X`;
   - `visual-public-last:X`;
6. delete `mods WHERE id=X`;
7. rely on declared FK cascades/set-null behavior for owned children;
8. optionally clean empty family containers only after their intended semantics are characterized.

Then change `DuplicateCleanupService` to call that retirement boundary instead of raw `DELETE FROM mods`.

### Important separation from D1

The deep SQLite audit's D1 still applies:

- filesystem move happens before DB deletion;
- crash/failure between them needs compensation or durable recovery.

This lifecycle checkpoint does **not** replace D1.

A future workflow needs both properties:

1. crash-safe move/delete coordination;
2. complete semantic retirement once the DB transition commits.

The deep SQLite audit remains the authority for property 1.
This audit is the authority for property 2.

---

# 14. Schema evolution guidance

Potential later schema changes should be deliberate.

## Good FK candidates, after migration characterization

`resource_providers.mod_id -> mods(id)`

could plausibly use `ON DELETE CASCADE` if the product contract is “a provider pin exists only while that provider exists.”

But legacy databases may already contain orphans, so migration must:

- inventory existing rows;
- resolve/quarantine/report invalid pins;
- only then enforce the constraint.

## conflict_rules

Blanket FK CASCADE is more nuanced because:

- ExactWinner uses winner only;
- pair rules have left/right/winner relationships;
- profile_rules references rule IDs;
- preservation-across-reinstall might be a product choice if identity becomes incarnation-safe later.

A centralized retirement boundary can close current semantics without immediately forcing a schema migration.

## Historical/current split

Do not add cascade FKs to historical audit columns merely for “clean foreign_key_check.”

Historical references need a nullable/deleted-entity presentation strategy, not erasure.

---

# 15. Identity-model implication

The deeper reason stale live state can reactivate is that the current local mod ID represents:

> “whatever package currently occupies this absolute source folder”

rather than a cryptographic/package incarnation.

That identity choice is already known by the backup/portability audit to be non-portable across roots.

This audit adds a separate consequence:

> identity is also **reusable within the same root** after deletion.

Changing global mod identity is high risk because profiles, rules, provenance, families, trust, issues, manifests, backups, and history all use the ID.

Therefore do **not** solve this audit by changing the ID format in the first checkpoint.

Close lifecycle semantics first.

A future versioned identity design could introduce:

- stable persisted entity UUID;
- separate current source path;
- package/content incarnation ID;
- explicit carry-forward policy for user decisions.

That is future architecture, not the first fix.

---

# 16. Learned-rule consequence

This audit justifies a durable rule:

> **Entity deletion is not complete when only FK-owned rows disappear. Retire every live semantic reference that can affect a future entity reusing the same identity, while preserving historical evidence separately.**

This is especially important when identifiers are deterministic from mutable/reusable external locations such as filesystem paths.

The rule is recorded as LR-007 in this support branch.

---

# 17. Verification actually performed

Performed:

- re-verified canonical GitHub `main` before task selection and again after the support-integration advance;
- read the permanent continuity/read order and active LR-001 through LR-006;
- inspected open PRs and non-PR branches to avoid duplicate work;
- inspected the integrated support-audit inventory for overlap;
- inspected actual bodies for:
  - `DuplicateCleanupService.ArchiveSafeAsync`;
  - `CatalogService.RefreshFoldersAsync`;
  - `ManagerDatabase` relevant write/settings/family methods;
  - `Schema.Sql`;
  - `PlannerSnapshotRepository.LoadAsync`;
  - `DeploymentPlanner.Build`;
  - `ConflictRuleIndex`;
  - `ConflictEngine.Decide`;
  - `HealthService.ScanAsync`;
  - Nexus mod-scoped settings;
  - legacy rule/resource-provider import;
- inspected relevant Automation and Hardening test assertions;
- compared with the specialized deep SQLite and backup/portability audits;
- checked current SQLite primary documentation for `integrity_check` vs `foreign_key_check`.

Not performed:

- no local checkout / `git status` was available through this chat;
- no SQLite runtime reproduction of delete/reimport resurrection;
- no `dotnet test`;
- no Windows-specific execution;
- no hosted Windows Release Gate;
- no verification-cache promotion;
- no production code or schema modification.

Therefore:

- “confirmed” above means directly implied by current source/schema/control flow;
- no claim is made that a user has already encountered the resurrection path;
- a future source fix must earn fresh exact verification.

---

# 18. Parallel-agent integration notes

## SQLite transaction audit

`SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` remains the specialized authority for:

- DuplicateCleanup move-before-delete crash/failure;
- transaction ownership;
- filesystem/DB atomicity;
- recovery/compensation.

Do not replace its D1 with this audit.

## Backup/portability audit

`STATE_BACKUP_PORTABILITY_RECOVERY_AUDIT.md` remains the authority for:

- cross-root local-ID instability;
- recovery capsule design;
- path rebasing;
- preserving existing IDs across restore.

This audit adds the same-root **ID reuse after deletion** consequence.

## Windows filesystem audit

That audit owns:

- reparse traversal;
- CAS object trust;
- physical containment;
- native replacement behavior.

Mod retirement must not weaken any of those invariants.

## Smart Pack branch

Provider-neutral Smart Pack work is separate. If it later introduces source deletion/replacement or automatic library eviction, it must call the same lifecycle/retirement boundary instead of issuing raw mod-row deletes.

## Integration agent

The support-audit integration on canonical main has already merged the previous documentation set and renumbered rules through LR-006. This branch starts from that state and adds only the new lifecycle audit + continuity references.

---

# 19. Successor handoff

If implementing this finding:

1. verify actual canonical `main` and active PRs first;
2. read the permanent continuity constitution and LR-001 through LR-007;
3. read this audit plus the deep SQLite transaction audit;
4. begin with characterization/regression tests, especially delete -> same-path reimport -> planner behavior;
5. implement **one centralized retirement boundary** only;
6. do not combine it with a global identity migration, broad schema rewrite, D1 crash-journal redesign, backup/restore, or Smart Pack acquisition;
7. preserve historical audit records separately from live configuration;
8. require the current deployment manifest to be safe before retirement;
9. run the exact full Windows Release Gate for any production change;
10. update durable handoff state and explicitly require your successor to inherit and recursively pass the continuity constitution to the agent after them.

**Do not break the chain.**


---

# 20. Late upstream reconciliation

During the final concurrency check, canonical `main` advanced from the audit's
task-lock base `a6cfef0bb161a7846cfab7c0761f9f4d90ea46e1` to:

`5619604e88a27176726ada8518f53d385abc7b0f`

That new commit changes only `_AGENT_CONTEXT/VERIFICATION.md` to record
connector-side structural verification of the prior support-audit integration.
It explicitly preserves the same closed product evidence and still requires a
fresh hosted Windows gate for the integrated documentation state.

It changes no production source, tests, schema, deletion path, identity
generation, planner inputs, settings behavior, or lifecycle conclusion in this
audit.

This support branch deliberately does not rewrite or duplicate that newer
verification record. Integration should preserve canonical main's
`VERIFICATION.md` and apply this audit on top.
