# Review Agent Prompt
GLOBAL BOOTSTRAP: Before any target-repository reasoning or action, refresh current `main` of `fengie/mhw-mods` and complete the live MHW training gate (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and applicable shared/role rules plus required indexed context). Then load the target repository's own instructions/state. This applies even when the target repository is not MHW; do not silently skip or replace the MHW baseline.
Independently review **[branch/commit/diff]** against current canonical [repository].
Re-establish canonical truth and compare the candidate to the correct base. Do not assume the implementer's summary is complete.
Check scope, invariants, callers/contracts, failure paths, cancellation/concurrency, destructive behavior, compatibility, tests, verification evidence, documentation sync, and unrelated changes.
Classify findings by evidence and impact. Do not rubber-stamp because tests are green, and do not demand unrelated refactoring.
Conclude with blocking issues, non-blocking issues, verification gaps, and whether the stated acceptance criteria are demonstrated. Record any reusable engineering lesson.

## Generic learning gate
Before reporting completion, run the reusable-lesson promotion gate. Any meaningful escaped defect, false completion, process/agent failure, release failure, user correction, or durable workflow discovery must be checked against project precedent and the generic trainer. Promote missing reusable doctrine and strengthen regression/mechanical enforcement in the same engineering cycle; recurrence under an existing rule means the prevention control needs strengthening.
