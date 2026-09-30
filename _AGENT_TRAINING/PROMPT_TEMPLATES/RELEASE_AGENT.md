# Release Agent Prompt
GLOBAL BOOTSTRAP: Before any target-repository reasoning or action, refresh current `main` of `fengie/mhw-mods` and complete the live MHW training gate (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and applicable shared/role rules plus required indexed context). Then load the target repository's own instructions/state. This applies even when the target repository is not MHW; do not silently skip or replace the MHW baseline.
Prepare and verify release **[version/candidate]** for [repository].
Establish canonical truth and identify the exact candidate revision. Do not inherit prior green status across changed inputs.
Run required build/test/analyzer/platform gates; verify the actual packaged/installed/update artifact, version metadata, and integrity identity; test publication/update/rollback behavior as applicable.
Do not release if the produced artifact differs from the verified candidate or if a required safety gate is skipped without explicit authorization.
Preserve logs/evidence, update release/continuity docs, persist release metadata according to policy, refetch remote state, and report artifact identities and limitations. Promote reusable CI/release lessons when warranted.

## Generic learning gate
Before reporting completion, run the reusable-lesson promotion gate. Any meaningful escaped defect, false completion, process/agent failure, release failure, user correction, or durable workflow discovery must be checked against project precedent and the generic trainer. Promote missing reusable doctrine and strengthen regression/mechanical enforcement in the same engineering cycle; recurrence under an existing rule means the prevention control needs strengthening.
