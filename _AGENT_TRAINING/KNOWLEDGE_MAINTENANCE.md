# Knowledge Maintenance Policy

The trainer is a living engineering system. Maintaining it is part of normal development work.

## Promotion test
Promote a project lesson into company doctrine only when it is:
- **Reusable:** plausibly applies beyond one product.
- **Meaningful:** prevents a real failure class or materially improves engineering.
- **Understood:** root cause is known, not only the symptom.
- **Non-duplicate:** it adds to or improves existing doctrine.
- **Operational:** an agent can act on it and another agent can verify it.

## Required task-end question
Every engineering agent asks:

Did this task teach us something reusable?

If no, do nothing. If yes, check whether it is already documented. If absent, generalize, verify, and add it. If partially covered, improve the existing rule instead of duplicating it.

## When to add
Add a lesson after a concrete incident, verified experiment, repeated coordination problem, safety finding, release failure, or workflow improvement demonstrates a durable pattern.

## When to modify
Improve a rule when new evidence narrows its scope, identifies better enforcement, or reveals an important exception.

## When to merge
Merge overlapping lessons when they express the same invariant and separate entries add no operational value. Preserve useful provenance.

## When to supersede or remove
Do not silently erase history when doing so would hide why a rule existed. Mark obsolete doctrine as superseded and link to the replacement. Remove only noise, duplication, or guidance proven no longer applicable.

## Company vs project layer
Company layer: reusable principles, pipelines, role standards, verification, recovery, coordination, prompting.
Project layer: current architecture, branches, bugs, tests, revisions, environment specifics, next steps.

Do not copy transient project state into the company layer.

## Provenance
Major rules may retain lightweight provenance: observed failure class, root cause, generalized rule, and enforcement/verification. Keep product names, hashes, and historical detail out of the rule body unless necessary to understand provenance.

## Synchronization rule
When code changes architecture, invariants, verification, or operating truth, update the corresponding documentation at the same checkpoint. Do not defer important truth until the end of a long session.

## Documentation checkpoint
Ask after meaningful changes:
1. Did repository truth change?
2. Did architecture/invariants change?
3. Did verification status change?
4. Did a new failure mode appear?
5. Did priorities change?
6. Does handoff state need updating?
7. Did a reusable lesson emerge?

Update only what changed.

## Commit discipline
When practical, commit doctrine changes caused by a code change with that code and regression test. Large independent trainer improvements may be separate focused commits. Push meaningful knowledge checkpoints so institutional learning is not trapped locally.

## Mandatory continuous promotion loop
Do not wait until project end. On every meaningful incident or durable improvement:
1. record project-local evidence and root cause;
2. name the violated invariant/missing control;
3. check generic trainer coverage;
4. update absent/weak generic doctrine immediately;
5. update provenance when the evidence materially motivates the rule;
6. strengthen mechanical enforcement where practical;
7. only then close the learning portion of the incident.

Every escaped bug, regression, false completion, or repeated operational failure is evidence that prevention was missing or insufficient. The repair should normally include root-cause correction, regression/deterministic reproduction, sibling defect-class scan, project precedent, generic promotion review, and stronger automated enforcement when practical.

At milestones and major handoffs, audit the project learned-rule ledger against the generic trainer. Every active reusable rule must be represented in generic doctrine, explicitly project-specific with a reason, or pending promotion with an owner.
