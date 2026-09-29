# Auto Modder threat model

Status: M0 baseline. This document is normative for future Auto Modder work.

## Protected assets

- user files outside the manager-owned build area;
- live Monster Hunter: World installation state;
- manager database and imported immutable mod sources;
- credentials and private paths;
- process integrity;
- deterministic provenance needed for support and rollback.

## Untrusted inputs

Treat recipe packs, recipe parameter values, source files, catalogs, and generated package metadata as untrusted data. Executable adapters are trusted code only when explicitly registered by the shipped application or a future signed/allowlisted mechanism.

## Threats and required controls

### Arbitrary code execution

Recipes may not launch processes, invoke scripts, load assemblies, use reflection as an evaluator, or call arbitrary functions. The implemented v1 expression resolver accepts only whole-value input/property references.

### Path escape

Recipe outputs are manager-relative and pass existing PathRules normalization. Rooted, traversal, device-name, stream, and unsupported-root paths fail closed. BuildSandbox resolves the final absolute path under one manager-owned root and rejects lexical escape again.

Physical reparse/symlink validation remains required at publication and any boundary where an attacker could replace manager-owned directories. Auto Modder must reuse the repository's existing conservative filesystem doctrine rather than invent a weaker one.

### Resource exhaustion

The recipe engine caps input, step, and output counts. BuildSandbox separately caps output file count and total bytes. Future adapters must add source size, parsed record count, nesting depth, catalog size, diff size, and per-format limits.

### Adapter confusion

A recipe may only reference declared adapter IDs. Registry lookup is exact/case-sensitive. Declared version ranges must match. Each typed operation must be explicitly supported by that adapter.

### Source ambiguity

Future source resolvers must record exact source fingerprints and game-build applicability. A missing expected record, failed old-value assertion, malformed/truncated source, or unknown format blocks publication.

### Partial build residue

BuildSandbox writes to a unique temporary file and moves it into place only after the write completes. Failed/canceled writes remove owned temporary files best-effort and release their reserved resource budget. Duplicate output paths are rejected.

### Provenance spoofing

Generated manifests are manager-authored. They record recipe/version, adapter versions, source fingerprints, inputs, game build when known, and output fingerprints. Portable manifests must not contain credentials or unnecessary absolute personal paths.

## Non-bypass rule

Auto Modder output never writes directly to the live game directory. A completed generated package must enter the normal library, conflict, staging, deployment, and Undo/rollback pipeline.
