# Continuity Validator Adversarial Hardening — 2026-09-28

## Canonical base and scope

Canonical `main` was rechecked repeatedly and the final branch parent at this checkpoint is exact commit `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`.

This support checkpoint is intentionally limited to:
- `scripts/Test-AgentHandoff.ps1`
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1`

It does not change product C#, updater/archive behavior, Agent Control, frontend UI, SQLite state, filesystem mutation, release publication, or verification caches.

The task was selected because the integrated verification-infrastructure audit already documented a concrete gap: keyword-presence checks could accept contradictory or dead continuity text. Active product lanes were occupied, so this verification-only seam was independently safe.

## Existing strengths

The pre-existing gate already validated required handoff files, canonical repo/branch and version agreement, cache schemas, recursive-successor concepts, protocol-before-Learned-Rules order, and four negative fixtures for deleted required phrases. Those checks remain intact.

## Confirmed weakness

The previous validator inspected raw Markdown and mostly required tokens to be present. It did not distinguish active prose from HTML comments, reject explicit negation, or scope read order to the actual `## Read order` section.

A focused legacy-predicate probe confirmed all four adversarial states retained the exact predicates the old validator depended on: comment-only Learned Rules path; negated successor propagation; Core Rule weakening while retaining the authorization phrase; and reversed active read order masked by decoy prose.

This is a validator-semantics defect, not evidence that canonical handoff text was already corrupted.
## Implementation

`Test-AgentHandoff.ps1` now strips HTML comments before semantic checks, rejects explicit successor/agent-after negation, rejects explicit Core Rule weakening without authorization, and validates protocol/Learned-Rules ordering only inside the active numbered `## Read order` section.

`Test-AgentHandoff-NegativeFixtures.ps1` adds four adversarial fixtures:
1. required Learned Rules path preserved only in an HTML comment;
2. successor propagation explicitly negated while old keywords remain;
3. Core Rules explicitly weakened while the authorization phrase remains;
4. active read order reversed while decoy prose preserves the old whole-file order.

The original four negative fixtures are preserved.

## Verification actually performed

On `heaven2` / Windows after final reconciliation to canonical `main` `151a370ef6c0b3d4e6b1d8a306576af1ae231c40`:
- `scripts/Test-AgentHandoff.ps1` — PASS (`Version=8.8.0; requiredFiles=81`).
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1` — PASS.
- Baseline copied handoff fixture — PASS.
- Eight negative handoff fixtures — all rejected.
- Focused legacy-predicate probe — all four new mutations retained the predicates used by the old validator.

Two intermediate failures improved the hardening itself: an over-broad Core-Rule matcher produced a false positive and was narrowed; the first negation matcher missed `successor must not ...` and was corrected. The complete suite was rerun green after both repairs.

No full `Verify-Release.ps1`, `Build-Release.ps1`, or hosted Windows Release Gate is claimed yet.

## Severity and risk

Severity: **P1 verification-governance integrity**, not a product-runtime P1. A future handoff could previously preserve required vocabulary while reversing its meaning, allowing recursive safety/verification obligations to decay across agents.
## Deliberately not changed

- No production source or application tests.
- No verification-cache values or promotion state.
- No updater/release workflow or product-priority ordering.
- No existing Learned Rule numbering.

No new Learned Rule is added: the durable lesson is already represented by the continuity constitution and the existing verification-infrastructure audit's adversarial-fixture checkpoint.

## Remaining verification / successor checkpoint

Because this changes verification infrastructure, an integrating agent must recheck live `main`, preserve newer continuity edits, rerun both handoff scripts, and run the normal exact-source verification/release gate before calling this checkpoint fully closed. Failed evidence must be preserved rather than weakening the validator.

If a future bypass is found, prefer a concrete adversarial fixture over increasingly broad regexes that create false positives.

## Parallel-work integration notes

This checkpoint began on `a8b581176aac0e6bcf09c049285ed40f4b2b392c`, was fast-forward reconciled through `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d`, rebased through hosted-evidence commit `3d625dfbda54b34848f4da7369c02f065bf00f09`, and then rebased onto `151a370ef6c0b3d4e6b1d8a306576af1ae231c40` after PR #68 archive-cleanup research landed. None of those upstream commits touched the validator scripts. The handoff suite was rerun after each reconciliation.

Preserve current updater, Agent Control v2, frontend, and archive-provenance lanes independently. Do not use this verification change to rewrite their branch-local continuity snapshots.

The successor inherits the permanent continuity constitution and active Learned Rules. Before finishing, it must require its successor to preserve and recursively propagate the same system to the agent after them.

**Do not break the chain.**