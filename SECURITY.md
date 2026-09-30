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
- NuGet direct/transitive vulnerability auditing is enabled and warnings are build failures.
- Dependabot tracks GitHub Actions and NuGet dependency updates.

## Release authenticity

Hash verification protects integrity relative to trusted metadata, but it is not a substitute for publisher identity. Windows release binaries should be Authenticode-signed with a stable trusted publisher identity before broad public distribution. Never weaken update verification or bypass Windows security warnings as a workaround.
