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

## Release authenticity

Hash verification protects integrity relative to trusted metadata, but it is not a substitute for publisher identity. Windows release binaries should be Authenticode-signed with a stable trusted publisher identity before broad public distribution. Never weaken update verification or bypass Windows security warnings as a workaround.

## Heaven Local Bridge authentication
The private relay repository ACL is a compatibility trust boundary, not the preferred execution-authentication boundary. Hardened deployments use per-job HMAC-SHA256 authentication: workers keep `HEAVEN_BRIDGE_HMAC_KEY` machine-local and Agent Control on heaven2 uses the matching `AGENT_CONTROL_HEAVEN_HMAC_KEY` to sign canonical job payloads. New signed jobs use the versioned `mhw-bridge-canon-v1` canonical format; workers retain legacy-signature verification for rollout compatibility. Secret key material must never enter queue, result, state, or repository files.

## Updater signing roadmap
The updater's immutable-release, size, SHA-256, path, staging, product-manifest, and rollback checks protect integrity relative to trusted release metadata. They do not by themselves protect a client if the release publication authority is compromised and an attacker can replace both payload and metadata. The next authenticity milestone is independently signed update metadata with an embedded verification key plus explicit key rotation/revocation, expiry/freshness, and rollback/freeze defenses.
