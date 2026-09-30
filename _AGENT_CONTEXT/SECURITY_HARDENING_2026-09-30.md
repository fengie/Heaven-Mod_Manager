# Security hardening follow-up — 2026-09-30

## Goal

Reduce the project's obvious, reviewer-visible attack surface with machine-enforced controls rather than security claims that depend on convention.

## Research basis

This pass cross-checked the repository against current guidance from GitHub Actions secure-use documentation, NIST SSDF, Microsoft's .NET archive-extraction guidance, and OWASP's MCP security guidance.

The common themes applied here are:

- minimize and separate privileges in CI;
- treat persistent self-hosted runners as durable trusted machines rather than disposable sandboxes;
- pin executable supply-chain dependencies immutably;
- prevent credentials from entering source control;
- validate untrusted archive paths and resource use before writes;
- use message-level authentication, freshness, and replay protection for agent/remote-execution control planes;
- turn discovered defect classes into regression gates.

## Findings repaired

### 1. Candidate code had repository write authority on a persistent runner

`.github/workflows/startup-performance-gate.yml` executed candidate code on the long-lived Heaven self-hosted runner while granting `pull-requests: write`. Its checkouts also retained GitHub credentials in local Git configuration.

Repaired:

- workflow token scope is now `contents: read`;
- both candidate and baseline checkouts use `persist-credentials: false`;
- the PR-comment mutation was removed from the code-execution job;
- benchmark evidence remains available through the workflow summary/artifact.

### 2. Persistent-runner checkout credentials were not universally forbidden

The CI policy previously required `persist-credentials: false` only for self-hosted pull-request validation.

Repaired:

- every `actions/checkout` use on a persistent self-hosted runner is now required to disable persisted credentials;
- non-allowlisted persistent-runner workflows cannot carry top-level write scopes;
- only the documented mutation/release workflows remain in the write allowlist.

### 3. Credential material lacked an index-level regression gate

Repaired:

- `scripts/testing/Test-TrackedSecretLeaks.ps1` scans tracked files for high-confidence credential/private-key patterns;
- common local environment, credential-container, and signing-key files are ignored in `.gitignore`;
- `Test-CiSecurityPolicy.ps1` invokes the tracked-secret gate;
- security doctrine now requires both pre-staging ignore rules and an index-level fail-closed scan.

### 4. Security policy gate was left malformed during concurrent edits

A concurrent edit exposed an important process failure: the security policy itself temporarily became malformed.

Repaired:

- `Test-CiSecurityPolicy.ps1` was rewritten as one coherent policy source;
- duplicate/corrupted fragments were removed;
- the standalone secret scanner's PowerShell interpolation defect was fixed;
- future security-policy changes remain subject to the same release/security verification path.

This is a bug-prevention precedent: security controls are executable production policy and must be parser/execution-tested, not reviewed only as text.

## Existing controls re-verified by source review

- updater network requests are restricted to HTTPS `api.github.com`;
- downloaded update artifacts are size-bounded and SHA-256 verified;
- updater archives are manually iterated with entry-count and extracted-byte budgets;
- updater paths are normalized and constrained under trusted roots;
- traversal, Windows device/ADS ambiguity, reparse points, symlinks, duplicate/case-colliding entries, and staged file-set mismatches are rejected;
- external GitHub Actions in current workflows are pinned to full 40-character commit SHAs;
- current persistent self-hosted workflow checkouts use `persist-credentials: false`;
- Agent Control defaults to loopback and refuses an unauthenticated non-loopback bind;
- dependency auditing remains enabled for direct and transitive NuGet dependencies.

## Material remaining risks

### A. Heaven Local Bridge execution authentication

Observed live on both `heaven` and `heaven2`:

- worker is elevated;
- `auth_mode` is `private-repo-acl`;
- raw/process/filesystem/desktop actions are available.

Therefore repository write authority is still sufficient to enqueue administrator-capable execution. HMAC support exists in the worker and Agent Control, but it is not active on the live workers.

Do not label this boundary fully hardened until execution-capable jobs require a machine-local cryptographic authority with freshness/replay protection. Direct ChatGPT relay compatibility currently depends on ACL-mode jobs, so enforcement must be migrated without silently breaking the operator path.

### B. `main` is still unprotected

The GitHub repository API reports `main` as unprotected. The minimum compatible policy should block force-push and branch deletion while preserving the owner's direct-main workflow; stronger required checks can be added after runner reliability is stable.

The currently available GitHub connector does not expose branch-protection/ruleset mutation, and the attempted local authenticated admin path was blocked by the execution safety boundary. This remains an external repository-setting action rather than a source-code fix.

### C. Publisher authenticity

Artifact SHA-256 validation protects integrity relative to trusted release metadata, but payload and metadata can still be replaced together if publication authority is compromised. Broad public distribution should add independently verifiable update metadata and a stable Windows publisher-signing identity with rotation/revocation procedures.

## Standing acceptance criteria

Security hardening is not complete merely because documentation exists. A change is acceptable only when:

1. the trust boundary is stated explicitly;
2. the preventive control is implemented;
3. a regression check covers the defect class where practical;
4. the control itself parses/executes on the target platform;
5. exact-main evidence is captured;
6. unresolved external controls are named rather than represented as complete.
