# Release Agent Prompt
Prepare and verify release **[version/candidate]** for [repository].
Establish canonical truth and identify the exact candidate revision. Do not inherit prior green status across changed inputs.
Run required build/test/analyzer/platform gates; verify the actual packaged/installed/update artifact, version metadata, and integrity identity; test publication/update/rollback behavior as applicable.
Do not release if the produced artifact differs from the verified candidate or if a required safety gate is skipped without explicit authorization.
Preserve logs/evidence, update release/continuity docs, persist release metadata according to policy, refetch remote state, and report artifact identities and limitations. Promote reusable CI/release lessons when warranted.
