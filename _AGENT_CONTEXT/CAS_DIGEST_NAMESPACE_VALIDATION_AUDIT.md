# CAS digest namespace / identifier validation audit — 2026-09-27

## Status

Documentation-only autonomous support checkpoint.

Canonical `main` inspected before branch creation:

`d001870d4cd3549841d8511392ae7885f174bca2`

Support branch:

`agent/support-cas-digest-namespace-audit-20260927`

No production C#, tests, schema, workflows, verification caches, or active CAS concurrency behavior are changed by this checkpoint.

## Why this lane was selected

The first support lane selected in this session was CAS root/leaf filesystem identity and reparse policy. Before this agent published anything, a parallel branch appeared at:

`agent/support-cas-filesystem-identity-audit-20260927` @ `21164da2fb5812b3c2631cb9a6ec990206b08453`

That branch already completed the root-junction / leaf-reparse / migration-hardlink audit. The support-agent rules require reselection rather than duplication.

While tracing that boundary, a distinct issue surfaced that the parallel audit does not cover:

> strings treated as SHA-256 identifiers are not consistently validated as exactly 64 hexadecimal characters before they are combined with the CAS root and used as filesystem paths.

This checkpoint therefore owns only **digest namespace / identifier validation**.

## Internal assignment

### Scope

Inspect the exact current source paths that:

- derive a filesystem pathname from a purported SHA-256 digest;
- persist or reload blob identifiers;
- import legacy digest strings;
- pass persisted digest strings into CAS existence/restore paths;
- define tests for malformed digest behavior.

### Questions

1. Does `BlobStore.PathFor` prove its input is a SHA-256 digest before path composition?
2. Can a rooted or relative-traversal token escape `BlobStore.Root`?
3. Can legacy migration perform filesystem work outside its `Blobs` directory before final SHA verification rejects a malformed token?
4. Do SQLite schema/repositories constrain persisted blob identifiers?
5. Does current staged-byte verification prevent malformed identifiers from silently publishing wrong live bytes?
6. What exact regression tests define the smallest safe future change?

### Exclusions

- no CAS sharing-violation retry changes;
- no root/leaf reparse implementation;
- no hardlink migration redesign;
- no recursive ModScanner/adoption/Smart Inbox containment;
- no SQLite schema migration in this support branch;
- no verifier/cache promotion.

### Success criteria

Leave a durable current-source audit that:

- distinguishes confirmed source behavior from runtime-unreproduced risk;
- preserves the strength of the current CAS content-integrity checkpoint;
- gives exact regression fixtures and a narrow implementation order;
- can be implemented by a successor without this chat.

## Methodology

Inspected current bodies in:

- `src/MhwModManager.Filesystem/BlobStore.cs`
- `src/MhwModManager.Filesystem/AtomicFileOps.cs`
- `src/MhwModManager.Filesystem/DeploymentExecutor.cs`
- `src/MhwModManager.Filesystem/ModScanner.cs`
- `src/MhwModManager.Diagnostics/HealthService.cs`
- `src/MhwModManager.Storage/LegacyV7Migrator.cs`
- `src/MhwModManager.Storage/ManagerDatabase.cs`
- `src/MhwModManager.Storage/PlannerSnapshotRepository.cs`
- `src/MhwModManager.Storage/Schema.cs`
- `src/MhwModManager.Core/Domain.cs`
- `src/MhwModManager.Core/DeploymentPlanner.cs`
- `tests/MhwModManager.IntegrationTests/BlobIntegrityTests.cs`
- `tests/MhwModManager.IntegrationTests/HardeningTests.cs`
- current continuity, filesystem, migration, and CAS checkpoint documents.

Primary platform reference:

- .NET `Path.Combine`: <https://learn.microsoft.com/dotnet/api/system.io.path.combine>

Microsoft documents that if a later `Path.Combine` argument is rooted, prior path components are discarded. The API also does not itself establish that an arbitrary subsequent string is a safe relative filename component.

## Existing strengths that must be preserved

### Current capture produces canonical SHA tokens itself

Normal `BlobStore.CaptureWithHashAsync` computes SHA-256 from captured bytes and converts the digest to lowercase hexadecimal before calling `PathFor`.

That path is structurally safe because the token is produced by the cryptographic API, not parsed from persistence or user-controlled state.

### Restore now validates the bytes actually staged for publication

Current `BlobStore.RestoreAsync` passes the advertised digest into `AtomicFileOps.ReplaceFromAsync`.

`AtomicFileOps`:

1. copies the CAS source into a private same-destination-directory temp;
2. flushes it;
3. rewinds the still-exclusive staged file;
4. computes SHA-256;
5. refuses publication if staged bytes do not equal the advertised digest.

Therefore a malformed token containing path syntax cannot make arbitrary out-of-CAS bytes silently become valid live content: the actual 64-hex SHA-256 of those bytes cannot equal a malformed pathname token.

This is an important severity limiter and must not be weakened.

### Existing valid uppercase restore behavior is intentional

`BlobIntegrityTests.Concurrent_valid_captures_converge_and_restore_the_expected_bytes` restores with `hash.ToUpperInvariant()`.

Any future digest validator should preserve case-insensitive 64-hex acceptance and canonicalize to lowercase for path lookup.

## Findings

### P1 — legacy migration can perform filesystem work outside its CAS `Blobs` directory before malformed hashes fail final verification

**Confirmed source behavior; exact runtime fixture not executed in this documentation-only environment.**

`LegacyV7Migrator` imports digest strings from legacy JSON.

For mod-file hashes it currently does:

- lowercase the string;
- check only `hash.Length != 64`;
- call `EnsureLegacyBlob(hash)`.

For `expected` entries it also checks only length 64.

For `bases`, a nonblank hash is lowercased and passed to `EnsureLegacyBlob` without even the 64-character length check.

`EnsureLegacyBlob` then performs:

- `Path.Combine(LegacyBlobs, hash)`;
- `File.Exists(src)`;
- `Path.Combine(nextBlobRoot, hash)`;
- if destination is absent, `CreateHardLinkW(dst, src)` or `File.Copy(src, dst, false)`.

Only **later**, after the import work, does migration iterate referenced hashes, open `Path.Combine(nextBlobRoot, hash)`, compute SHA-256, and compare the real digest to the original string.

A 64-character token can contain relative path syntax while still passing the length-only checks. For example, a token shaped like:

`..\<61-character-name>`

has total length 64 but is not a SHA-256 digest.

With the current directory layout:

- `LegacyBlobs\..\name` resolves outside legacy `Blobs`, into the legacy V2 directory;
- `nextBlobRoot\..\name` resolves outside new `Blobs`, into the new `Next` directory.

If the crafted source path exists and the corresponding destination path does not, `EnsureLegacyBlob` can create/link/copy that file **outside the CAS directory** before final digest verification rejects the malformed identifier.

The final authoritative SHA check still prevents migration from being marked complete, because a real SHA-256 hexadecimal digest cannot equal a string containing path separators. But the failed migration cleanup resets database import state; it does not remove arbitrary physical files created outside the CAS directory.

This is a namespace/side-effect safety defect, not a claim of arbitrary overwrite: the current hardlink/copy path uses no-overwrite behavior and the described malformed migration ultimately fails.

### P1/P2 — `BlobStore.PathFor` treats an identifier as a path component without validating that it is an identifier

**Confirmed source behavior.**

Current implementation:

`return Path.Combine(Root, sha.ToLowerInvariant());`

There is no:

- exact length check;
- ASCII hexadecimal check;
- rooted-path rejection;
- separator rejection;
- `.` / `..` rejection;
- full-path containment assertion.

For normal capture-generated digests this is harmless because the input is known-good.

For any persisted/imported/caller-provided token, it means the API has two meanings at once:

- content identifier;
- filesystem path fragment.

Microsoft documents that a rooted later argument to `Path.Combine` discards the earlier root. Relative parent segments are also interpreted by filesystem path resolution.

A CAS API should not allow a content identifier to select path structure at all.

### P2 — persisted blob references are text, and current read paths do not revalidate digest shape

**Confirmed source behavior.**

The SQLite schema stores blob references as ordinary `TEXT` fields, including:

- `blobs.sha256`;
- `mod_files.blob_sha256`;
- `original_files.blob_sha256`;
- `deployment_manifest.blob_sha256`;
- `deployment_manifest.expected_live_sha256`.

No inspected schema constraint requires 64 hexadecimal characters.

`PlannerSnapshotRepository`, `ManagerDatabase.GetModFilesAsync`, and scanner cache loading read those strings directly into domain records.

Those records then flow into callers such as:

- `DeploymentPlanner` -> `DeploymentChange`;
- `DeploymentExecutor` -> `BlobStore.RestoreAsync`;
- `HealthService` -> `File.Exists(blobs.PathFor(...))`;
- `ModScanner` cache fast path -> `File.Exists(blobs.PathFor(...))`.

The most important current mitigation is again restore staged-byte verification: a malformed identifier should fail before wrong live bytes are published.

But health/cache existence checks can still probe a pathname selected by malformed persisted state, and malformed state is represented as an ordinary string rather than rejected at the CAS boundary.

This is primarily corruption/fail-closed robustness, not evidence of a remote attacker path.

### P2 — malformed digest behavior has no focused regression coverage

The current `BlobIntegrityTests` cover:

- corrupt existing object rejection;
- corrupt restore rejection;
- concurrent valid captures;
- canceled capture cleanup;
- deployment and startup-recovery behavior around corrupt CAS bytes.

No inspected test covers:

- non-hex 64-character digests;
- parent traversal in a digest token;
- rooted digest tokens;
- separators in digest tokens;
- migration rejection before `EnsureLegacyBlob` side effects;
- malformed persisted digest state flowing through health/scanner/planner paths.

The suite can therefore prove byte integrity while leaving the identifier namespace contract undefined.

## Severity boundaries

### What this audit confirms

- digest shape is not centralized or consistently validated before path composition;
- legacy migration length-only / no-length paths can reach `EnsureLegacyBlob`;
- `EnsureLegacyBlob` uses the token as a path fragment before final SHA validation;
- DB/domain read paths can carry arbitrary text as a blob identifier;
- `PathFor` itself accepts arbitrary strings.

### What this audit does not claim

- no successful deployment of bytes whose SHA does not match the advertised valid digest;
- no arbitrary overwrite of an existing file outside CAS;
- no remote exploit path;
- no runtime reproduction of the crafted legacy migration fixture;
- no bypass of the newly added staged-byte SHA verification.

## Recommended regression-first checkpoint

Keep this separate from both the active CAS concurrency gate and the parallel reparse/hardlink branch.

### 1. Central digest parser / normalizer contract

Define one helper or value seam whose contract is:

- exactly 64 characters;
- ASCII `0-9`, `a-f`, `A-F` only;
- case-insensitive input;
- lowercase canonical output;
- rejection happens **before** any filesystem path operation.

Do not treat "path happens to remain under root" as sufficient. A SHA identifier should have no path syntax by construction.

### 2. `BlobStore` boundary tests

Add tests such as:

- `PathFor_rejects_parent_traversal_digest`;
- `PathFor_rejects_rooted_digest`;
- `PathFor_rejects_non_hex_64_character_digest`;
- `PathFor_accepts_uppercase_valid_digest_and_canonicalizes_lowercase`;
- `Restore_invalid_digest_fails_before_creating_destination_staging`.

For invalid restore inputs assert:

- destination remains unchanged/absent;
- no `.mhwmm.tmp` file is created;
- no out-of-root source path is trusted.

### 3. Legacy migration regression

Create a minimal legacy fixture with a malformed 64-character traversal token and a sentinel file at the path that old `EnsureLegacyBlob` would resolve.

Expected future contract:

- migration rejects the malformed digest before `EnsureLegacyBlob`;
- no file/link is created under `State\Next` outside `Blobs`;
- no blob row / mod file / manifest reference is committed for that token;
- failure reporting identifies malformed legacy content rather than later generic blob mismatch.

Also cover malformed `bases`, because that path currently lacks the mod-file length check.

### 4. Persistence corruption test

Inject a malformed digest into a test database fixture and prove the first CAS-consuming boundary reports invalid persisted state instead of probing an arbitrary filesystem path.

Prefer central validation at the CAS/value boundary rather than relying only on every SQLite caller remembering to validate independently.

### 5. Preserve current content integrity

After identifier validation, retain all current SHA verification:

- existing-object capture verification;
- sharing/lock retry behavior;
- staged restore verification.

Identifier validation and byte verification defend different invariants.

## Implementation guidance

The smallest safe production shape is likely:

1. introduce one `NormalizeSha256` / digest value helper in a dependency location usable by filesystem + migration/storage;
2. validate/canonicalize inside `BlobStore.PathFor` so every CAS path lookup is protected;
3. validate legacy digest strings before any `Path.Combine`, `File.Exists`, `FileInfo`, hardlink, or copy call;
4. consider validating on persistence write/read boundaries for earlier corruption diagnostics;
5. do **not** add a broad DB migration until tests show it is needed for correctness;
6. keep uppercase compatibility;
7. keep this change independent of root-reparse and migration-hardlink semantic changes.

A schema `CHECK` may be useful later, but it cannot replace runtime validation because legacy JSON and direct CAS API inputs exist before/without a DB write.

## Learned-rule decision

No new Learned Rule is added by this support branch.

The permanent safety invariants already require safe path handling, and LR-004 already establishes that filesystem containment must be explicit rather than inferred from lexical composition. This audit specializes that existing rule for **identifier-derived path components** rather than creating a competing numbering entry while parallel agents are active.

## Durable relationship to parallel work

The new parallel document:

`_AGENT_CONTEXT/CAS_FILESYSTEM_IDENTITY_REPARSE_AUDIT.md`

is the specialized authority for:

- CAS root junction/reparse policy;
- CAS hash-leaf reparse identity;
- legacy hardlink aliasing;
- real Windows link fixtures.

This document is the specialized authority only for:

- SHA token shape;
- identifier/path namespace separation;
- malformed legacy/persisted digest handling.

The two checkpoints complement each other and should not be collapsed into one large production change.

## Verification actually performed

Performed:

- verified canonical GitHub `main` at `d001870d4cd3549841d8511392ae7885f174bca2` before branch creation;
- inspected recent CAS commits and the active concurrency repair;
- inspected open PR/branch activity;
- detected the parallel CAS filesystem-identity audit and reselected rather than duplicating it;
- inspected actual current CAS, atomic restore, migration, persistence, planner, health, scanner, schema, domain, and test bodies;
- confirmed current blob-integrity tests contain no malformed-digest fixture;
- checked Microsoft primary documentation for `Path.Combine` rooted-path behavior;
- kept all changes documentation/continuity only.

Not performed:

- no local checkout or `git status`;
- no PowerShell handoff validator;
- no `.NET` build/tests;
- no crafted migration runtime reproduction;
- no Windows path fixture;
- no hosted Windows Release Gate for this documentation-only branch;
- no verification cache promotion.

## Recommended next independent checkpoint

Do **not** interrupt the active CAS sharing-violation verification.

After that exact gate is closed, this digest-namespace issue is small enough for a focused regression-first checkpoint:

> add malformed/uppercase digest tests, centralize exact 64-hex normalization before any CAS path composition, add the legacy traversal fixture, run focused tests, then require the normal exact Windows Release Gate for the production change.

The reparse/hardlink checkpoint remains separate.

## Successor handoff

Read this audit together with:

- `_AGENT_CONTEXT/CAS_INTEGRITY_CHECKPOINT.md`;
- `_AGENT_CONTEXT/CAS_FILESYSTEM_IDENTITY_REPARSE_AUDIT.md` if integrated;
- `_AGENT_CONTEXT/WINDOWS_FILESYSTEM_SAFETY_AUDIT.md`;
- `_AGENT_CONTEXT/LEGACY_MIGRATION_RECOVERY_AUDIT.md`;
- LR-004 and current active Learned Rules.

The successor inherits the permanent continuity constitution. Before modifying the repository, inspect actual canonical `main`, preserve exact verification provenance, and keep one independently verifiable boundary at a time.

Before finishing, explicitly require the next successor to inherit, preserve, and recursively propagate the same constitution and active Learned Rules to the agent after them.

**Do not break the chain.**


## Final canonical reconciliation before PR

Canonical `main` advanced after this branch was created from `d001870d4cd3549841d8511392ae7885f174bca2` to:

`265d58d6a9d4ffb6ab62cde987ba6ad335eae05a`

The intervening commits are verification/evidence/continuity only:

- `e5325317cb7005bdf9d3082a033ab95e666ebbf9` — persist hosted Windows verification evidence;
- `265d58d6a9d4ffb6ab62cde987ba6ad335eae05a` — record hosted CAS repair verification, local gate pending.

No production source inspected by this audit changed.

Hosted Windows Release Gate `36367883836` is now green for exact production candidate `d001870d4cd3549841d8511392ae7885f174bca2` (repository verifier 25/25, Integration 89/89, strict build/analyzers and ReadyToRun publish PASS). The repository still keeps the CAS checkpoint open because the required fresh local Windows verification on the authorized runner remains pending.

Therefore this support audit remains valid without rebasing its source analysis, and it does not supersede the current instruction to finish the local CAS closure before starting another production change.
