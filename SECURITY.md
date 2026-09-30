# Security Policy

## Supported versions

Security fixes are made against the current release line. Users should update to the newest published build before reporting a problem that may already be fixed.

## Reporting a vulnerability

Do not publish exploit details in a normal issue, discussion, or social post before maintainers have had a reasonable opportunity to investigate and ship a fix.

When this repository is public and GitHub Private Vulnerability Reporting is enabled, use the repository's **Security** tab to submit a private report. Until then, use a private channel agreed with the repository owner rather than a public issue.

Include the affected version/commit, attack prerequisites, a minimal reproduction, expected impact, and evidence that the issue crosses a trust boundary. Do not include real credentials, access tokens, or other users' private data.

## Security properties maintained by the project

- Update downloads are restricted to the expected HTTPS GitHub API host, bounded by size, and SHA-256 verified before staging.
- Archive/update paths are normalized and constrained under trusted roots, including traversal, reparse-point, symlink, duplicate/case-collision, device-name, and resource-budget checks.
- GitHub Actions workflows declare explicit least-privilege permissions.
- Third-party GitHub Actions are pinned to full immutable commit SHAs.
- Persistent self-hosted runners reject fork pull-request code.
- Persistent self-hosted runner checkouts never persist GitHub credentials, and candidate-code jobs run without write-capable repository token scopes unless isolated into a separately trusted mutation workflow.
- NuGet direct/transitive vulnerability auditing is enabled and warnings are build failures.
- Dependabot tracks GitHub Actions and NuGet dependency updates.
- Tracked credential material is rejected by a repository security gate, and common local credential/signing files are ignored before staging.

## Release authenticity

Hash verification protects integrity relative to trusted metadata, but it is not a substitute for publisher identity. Windows release binaries should be Authenticode-signed with a stable trusted publisher identity before broad public distribution. Never weaken update verification or bypass Windows security warnings as a workaround.

## Heaven Local Bridge authentication
Repository write access is transport authority, not command-execution authority. Per-job HMAC-SHA256 authentication is required by default for privileged bridge jobs. Workers load the key from the machine-local `~/HeavenBridge/auth/hmac.key` file (or `HEAVEN_BRIDGE_HMAC_KEY` for compatibility), while Agent Control on heaven2 resolves the same machine-local file or its dedicated environment override. The key must never enter queue, result, state, log, or repository files.

New signed jobs must use the versioned `mhw-bridge-canon-v1` canonical format. Unsigned repo-ACL execution is available only through the deliberately named emergency switch `HEAVEN_BRIDGE_ALLOW_INSECURE_REPO_ACL_ONLY=1`, and legacy unversioned HMAC canonicalization requires `HEAVEN_BRIDGE_ALLOW_LEGACY_HMAC_CANONICAL=1`. Agent Control independently refuses unsigned submission unless `AGENT_CONTROL_ALLOW_INSECURE_UNSIGNED_BRIDGE=1` is explicitly set.

General-purpose child commands receive a secret-scrubbed environment. Token/password/secret/HMAC/API-key/credential-like host variables cannot be requested through `env_from_host`; one-time GUI credential entry remains on the separate encrypted, destination-bound secret-envelope channel.

## Updater signing roadmap
The updater's immutable-release, size, SHA-256, path, staging, product-manifest, and rollback checks protect integrity relative to trusted release metadata. They do not by themselves protect a client if the release publication authority is compromised and an attacker can replace both payload and metadata. The next authenticity milestone is independently signed update metadata with an embedded verification key plus explicit key rotation/revocation, expiry/freshness, and rollback/freeze defenses.
