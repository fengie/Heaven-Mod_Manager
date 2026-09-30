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

