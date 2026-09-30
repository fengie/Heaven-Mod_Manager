# Security and supply-chain doctrine

These rules are reusable across projects unless a stronger project-specific control supersedes them.

## CI dependencies are executable dependencies

Treat every GitHub Action as executable third-party code. Pin non-local Actions to a full immutable commit SHA, keep the readable release tag as a comment, and use automated dependency tooling to propose reviewed SHA refreshes. A movable major-version tag is not a security boundary.

## Persistent self-hosted runners are trusted machines

Do not execute fork pull-request code on a persistent self-hosted runner. A read-only `GITHUB_TOKEN` does not protect the host filesystem, cached credentials, other repositories, or later jobs if arbitrary PR code can persist on the machine. Prefer ephemeral isolated runners. If a persistent runner is unavoidable, reject untrusted/fork PRs before allocation and do not persist checkout credentials.

## Least privilege must be visible in source

Every workflow declares explicit top-level `permissions:`. Write scopes belong only in workflows/jobs that genuinely publish or mutate repository state, and should not be combined with untrusted source execution.

## Vulnerability auditing is a build invariant

Enable known-vulnerability auditing for direct and transitive dependencies. Treat unsuppressed findings at the chosen severity threshold as build failures. Any suppression must name the advisory, document reason/owner/expiry, and be removed when a fixed dependency is available.

## Integrity and identity are different controls

Hashes verify content integrity relative to trusted metadata; they do not identify the publisher if an attacker can replace metadata and payload together. User-distributed executables should pair hash verification with an appropriate publisher-signing mechanism whose signing identity is isolated from ordinary build execution.

## Security defects create regression rules

When a security weakness is found, fix the immediate defect, add a machine-enforced regression for the defect class, document the trust boundary/root cause, and promote the general lesson into reusable project/agent guidance.

## Runner and shell are one execution contract

A workflow runner migration is also a shell/runtime migration. When changing runner OS, labels, or default shell, inspect every inline script and tool invocation for platform assumptions and execute the policy on the target runner before calling the migration complete. Bash heredocs, path syntax, quoting, executable discovery, environment-variable syntax, and line endings are part of the security control when the workflow enforces security policy. A security gate that cannot execute on its declared runner is equivalent to no gate.

## Privileged release tools must be pinned and verified

Do not trust an arbitrary preinstalled executable or a moving `latest` download inside a privileged publication job. Pin the tool version and immutable asset URL, verify its cryptographic digest against independently reviewed release metadata before execution, and machine-enforce those invariants in the security gate.

## Policy scanners must be self-safe

When a security scanner is embedded in the configuration it scans, assume its own source text, comments, and diagnostics will be part of the input. Match structured configuration with anchored/parsed rules rather than raw substring presence, and include the scanner file itself in regression coverage so policy text cannot trigger false positives.

## Cross-language policy generation must be literal-safe

Security and release policy is executable source. When JavaScript, Python, shell, or another host language emits source for a different language, do not use replacement APIs whose replacement text has its own interpolation/metacharacter rules unless those rules are explicitly neutralized. Prefer callback/literal-safe replacement or structured generation, and make target-language parsing/execution part of the acceptance gate.

## PR validation has no privileged ambient capability

A PR validation workflow on a persistent runner must be secretless and read-only. Do not reference repository/action secrets or grant write token scopes in a workflow that executes PR-controlled source. Separate mutation, publication, deployment, or credentialed operations into a trusted push/post-merge workflow after the source has crossed the review/integration boundary.

## Remote execution bridges need an authority beyond repository write
If a repository-backed relay can execute arbitrary commands on a persistent host, repository write access is too broad to be the only execution authorization boundary. Add per-request cryptographic authentication whose secret/private material remains machine-local, include replay/freshness checks, version the canonical signing format, and keep relay payloads free of raw credentials.

## Update authenticity needs a separate trust root
Artifact hashes published by the same authority as the artifact detect corruption but do not survive compromise of that publication authority. Mature update systems authenticate metadata with an independent verification key and define rotation/revocation, freshness/expiry, and rollback/freeze behavior.

## Persistent runners must not retain checkout credentials

On a persistent self-hosted runner, every `actions/checkout` use must set `persist-credentials: false`, even for trusted push workflows. Leaving the token in local Git configuration creates a credential-reuse surface for later build tools, scripts, malware, stale worktrees, or compromised jobs.

## Separate untrusted computation from repository mutation

Benchmarking, compiling, testing, parsing, or otherwise executing candidate-controlled source should run with read-only repository permissions. Do not give that job write-capable `GITHUB_TOKEN` scopes merely for convenience such as posting a PR comment. Emit an artifact or step summary, then perform any necessary mutation in a separately reviewed trusted context that does not execute candidate source.

## Credentials must fail before review

Keep common local secret containers and environment files out of version control with ignore rules, but do not treat ignore rules as the control boundary. Add a tracked-file secret gate that scans the exact Git index for high-confidence credential formats and private-key material. The gate must scan its own policy files safely, fail closed, and never print the matched secret value.

## Privileged remote execution fails closed when authentication is missing
For a bridge that can run arbitrary commands or mutate files, transport membership is not execution authorization. Missing cryptographic authentication must disable privileged execution by default. Compatibility fallbacks must be explicit, conspicuously named as insecure/emergency behavior, and machine-gated so a missing configuration value cannot silently widen authority.

## Secret-bearing parent environments are not child-process defaults
General-purpose child processes must start from a scrubbed environment rather than inheriting the controller/worker environment wholesale. An `env_from_host` or similar allowlist is meaningless if the implementation first copies every host variable. Block secret-like variable names both from implicit inheritance and explicit forwarding; use a dedicated secret broker/envelope for credentials that genuinely need to cross a process boundary.

## Loopback services validate both Host and Origin

Binding an operator/control service to loopback is necessary but not sufficient for browser-adjacent local services. Validate the HTTP Host header against the expected loopback names/port and reject foreign browser Origin values before routing requests. Keep mutation APIs on loopback unless a separately authenticated remote boundary is intentionally designed, and add regression tests for hostile Host/Origin requests.

