# Security hardening pass — 2026-09-29

## Scope

Repository-wide review of updater/download trust, archive extraction, release publication, GitHub Actions, self-hosted runner exposure, dependency supply chain, diagnostics/secrets, and plugin/control-plane authorization.

## Research-backed conclusions

- GitHub documents full commit-SHA pinning as the immutable way to consume an Action release.
- GitHub warns that persistent self-hosted runners can be persistently compromised by untrusted pull-request code, including private repositories where read users can fork.
- GitHub recommends explicit least-privilege workflow permissions.
- NuGet supports direct/transitive known-vulnerability auditing; this repository now makes that policy explicit and treats findings as build failures through its existing warnings-as-errors rule.
- Microsoft recommends signing Windows application code with a trusted publisher identity for Smart App Control/SmartScreen trust.

## Findings and repairs

1. **Movable Action tags — repaired.** Current `uses:` references were changed from movable `@v4`/`@v7` tags to exact 40-character commit SHAs. Dependabot now tracks Action updates.
2. **Persistent self-hosted PR exposure — materially reduced.** PR validation jobs using the Heaven runner reject fork heads before allocation. Checkout credentials are not persisted in those PR worktrees. The branch-lifecycle writer no longer triggers from pull requests.
3. **Dependency vulnerability drift — hardened.** `NuGetAudit=true`, `NuGetAuditMode=all`, and `NuGetAuditLevel=low` are explicit. Existing `TreatWarningsAsErrors=true` makes unsuppressed NU1901-NU1904 vulnerability findings fail restore/build.
4. **Policy regression — hardened.** `security-supply-chain-gate.yml` rejects movable Action refs, missing explicit permissions, unsafe fork/self-hosted PR combinations, and unreviewed `pull_request_target` + checkout patterns.
5. **Disclosure process — added.** `SECURITY.md` establishes coordinated reporting and release-authenticity expectations.

## Existing controls verified

The updater already enforces immutable non-draft release selection, strict `https://api.github.com` host validation, manifest/artifact size budgets, SHA-256 artifact verification, safe archive extraction, path normalization, reparse/symlink rejection, duplicate/case-collision rejection, exact staged-file-set verification, per-file hashes, transactional backup/journaling, and rollback.

The Heaven control-plane/plugin layer uses opaque secret handles, scoped purposes/TTLs, permission brokerage, and fail-closed validation instead of transporting raw secret values through normal plugin payloads.

## Remaining controls outside normal source writes

- **Protect `main`.** At audit time the GitHub API reported `main` as unprotected. Configure an active ruleset/branch protection appropriate to the owner's workflow; at minimum block force-push/deletion and require security/release checks where operationally feasible.
- **Prefer ephemeral runners.** Fork guards materially reduce exposure, but a persistent runner is not equivalent to a clean single-job runner. Any future workflow that accepts less-trusted code should use ephemeral isolation.
- **Sign public Windows releases.** Before broad public distribution, configure a stable trusted Authenticode/Artifact Signing identity for release EXE/DLL/installer/helper binaries. Hash verification proves integrity relative to metadata; code signing adds publisher identity.
- **Enable private vulnerability reporting when public.** GitHub Private Vulnerability Reporting should be enabled before the repository is opened to outside researchers.

## Acceptance criteria

- non-local Action references are pinned to 40-hex SHAs;
- workflows declare explicit top-level permissions;
- persistent self-hosted PR jobs reject fork code before allocation;
- self-hosted PR checkouts do not persist Git credentials;
- transitive dependency auditing remains enabled;
- changes to these rules are machine-gated.

## Follow-up: bridge fail-closed authentication and child-process secret isolation
Current secure-by-default guidance and the live elevated bridge state exposed two high-impact trust-boundary gaps: missing HMAC silently fell back to repository ACL authorization, and the child-process environment allowlist was defeated by copying the complete parent environment first.

The bridge now requires HMAC by default, requires the versioned cross-language canonical format, supports a machine-local `~/HeavenBridge/auth/hmac.key`, and puts repo-ACL-only/legacy behavior behind explicit emergency switches. Agent Control independently refuses unsigned submissions by default. General-purpose child processes now receive a secret-scrubbed environment, with secret-like `env_from_host` requests rejected.

Verification requires missing-key rejection, legacy-canonical rejection, cross-language HMAC stability, child-environment secret stripping, and a reusable CI policy gate that prevents those protections from silently disappearing.
