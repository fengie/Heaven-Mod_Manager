# Knowledge Maintenance Policy

The trainer is an active control surface, not an append-only archive. Every persistent instruction has repeated context and maintenance cost.

## Default maintenance order

When improving training, prefer:

**delete → merge → rewrite → relocate → add**

- **Delete** obsolete, redundant, ceremonial, or unenforced guidance.
- **Merge** rules that express the same invariant.
- **Rewrite** a weak/specific rule into one clear operational invariant.
- **Relocate** history, examples, project state, and rare-domain detail out of mandatory startup.
- **Add** only when a material reusable gap remains.

## Promotion test

A new permanent rule is justified only when it is:

- reusable beyond one incident;
- materially likely to prevent failure or improve execution;
- based on an understood cause/invariant rather than a symptom;
- not already covered by existing doctrine;
- operational enough to verify;
- worth its recurring context cost.

If an existing rule partially covers the lesson, strengthen that rule or its automated enforcement instead of adding another.

## Project vs trainer

Generic layer: reusable execution, verification, safety, coordination, recovery, and prompting principles.

Project layer: architecture, current revisions, active work, product-specific invariants, incidents, evidence, machines, branches, and release state.

Do not copy transient project facts into generic training. Git history and evidence files preserve history; active instructions should preserve only what affects future decisions.

## Mechanical enforcement over prose

When a failure can be prevented by a test, type/contract, lint rule, verifier, CI gate, size budget, or deterministic tool, prefer that enforcement and keep the accompanying prose short.

Repeated escape of a documented failure class is evidence that the control—not the warning count—needs improvement.

## Maintenance review

For meaningful trainer changes, compare before and after for:

- required startup bytes/tokens;
- duplicated concepts;
- contradictory authority/read-order rules;
- number of mandatory documents;
- whether execution order and completion evidence are obvious;
- whether rare/domain-specific detail is indexed rather than injected universally.

Do not add examples unless they resolve a realistic ambiguity.
