# Permanent Agent Continuity Constitution

This document is a **Core Rule** for `fengie/mhw-mods`. It is the repository's permanent recursive continuity constitution.

Every agent that works on this repository inherits two responsibilities:

1. safely improve or investigate the codebase; and
2. preserve and transmit the engineering knowledge, verification state, operating rules, and exact continuation state needed by every future agent.

The second responsibility is part of the definition of done.

This obligation is **recursive and indefinite**. Every agent must pass these rules to its successor; that successor must pass them to the agent after them; and the chain continues for every future agent. A missing original chat, a small task, or a mature project does not make propagation optional.

Only explicit user authorization may remove or weaken a Core Rule.

**Do not break the chain.**

## 1. The repository, not chat, carries the rules

Canonical development state is GitHub `fengie/mhw-mods` on branch `main`.

A fresh agent with only the repository must be able to discover the rules and continue safely without prior chat history.

The durable hierarchy is:

- `AGENTS.md` — concise mandatory entry point;
- `NEXT-AGENT-START-HERE.md` — current handoff;
- `_AGENT_CONTEXT/CURRENT_REVISION.json` — machine-readable current state and exact verification applicability;
- `_AGENT_CONTEXT/PROJECT_PLAN.md` — canonical feature priorities, ownership, dependencies, progress milestones, and next actions;
- this file — permanent Core Rules;
- `_AGENT_CONTEXT/LEARNED_RULES.md` — append-only agent-discovered durable rules;
- `_AGENT_CONTEXT/README_FIRST.md` — remaining read order;
- current state, next steps, verification, research, architecture, decisions, known issues, and evidence documents.

Feature planning follows the same rule: `_AGENT_CONTEXT/PROJECT_PLAN.md` is the current-status authority. Detailed design docs, issues, PRs, CURRENT_STATE, NEXT_STEPS, and handoffs may preserve evidence/detail but must not become competing live roadmaps. Keep the root README summary synchronized.

Do not leave important project knowledge only in chat, private reasoning, terminal history, temporary logs, CI webpages, or unpushed files.

## 2. Mandatory pre-change start order

Before modifying code or durable project state, every agent must:

1. inspect actual canonical `main` HEAD;
2. inspect `git status` and preserve unexplained local work;
3. inspect recent relevant Git history/diffs;
4. read `NEXT-AGENT-START-HERE.md`;
5. read `_AGENT_CONTEXT/CURRENT_REVISION.json`;
6. read this constitution;
7. read active `_AGENT_CONTEXT/LEARNED_RULES.md`;
8. follow the remaining read order in `_AGENT_CONTEXT/README_FIRST.md`.

Repository state outranks stale prompt/chat descriptions.

## 3. Recursive propagation is immutable

Recursive propagation is itself a Core Rule.

Future agents may not:

- remove it;
- weaken it;
- reinterpret it as optional;
- replace it with “document when convenient”;
- assume another system will preserve it;
- stop propagating it because the project appears mature;
- stop propagating it because a task is small.

Only explicit user authorization may weaken or remove it.

Every future handoff must instruct the successor to inherit, read, preserve, and recursively propagate this constitution again, and require that successor to pass the same obligation to the agent after them.

If the chain is found broken or incomplete, repairing it becomes part of the current agent's task before handoff.

## 4. Propagation must be mechanically verifiable

`scripts/testing/Test-AgentHandoff.ps1` must fail if the continuity system disappears or loses required invariants.

It must verify concepts rather than brittle exact prose, including:

- `AGENTS.md` exists and points to the permanent constitution;
- `NEXT-AGENT-START-HERE.md` exists and passes the recursive obligation onward;
- `_AGENT_CONTEXT/LEARNED_RULES.md` exists and is linked from the entry/read order;
- the mandatory start/read sequence is represented;
- the repository is identified as canonical;
- a fresh next agent is told it must continue without previous chat history;
- recursive successor propagation is explicit;
- Core Rules are protected from weakening without explicit user authorization.

The negative-fixture harness `scripts/testing/Test-AgentHandoff-NegativeFixtures.ps1` must deliberately break key propagation invariants and confirm that the real validator rejects those broken fixtures.

Changes to continuity validation are verification-infrastructure work. Test them independently and do not hide them inside unrelated refactors.

## 5. Continuity review before completion

Before declaring a task complete, every agent must ask:

### Code
- What changed?
- What behavior or architecture changed?
- What important behavior was deliberately preserved?

### Verification
- What is actually verified?
- At what exact SHA/fingerprint?
- What is only inspected, compiled, locally tested, integration tested, Windows tested, release-gate verified, runtime/user tested, or released?
- Did production source change after the last authoritative gate?

### Knowledge
- What did I learn that would be expensive for another agent to rediscover?
- Did I find hidden coupling, transaction behavior, platform behavior, failure modes, performance traps, or rejected approaches?

### Git
- Is meaningful work committed?
- Is it pushed?
- Is the working tree understandable?
- Is anything valuable still only local?

### Handoff
- Can a fresh agent continue with zero previous chat history?
- Is the exact next action documented?
- Have I explicitly required the next agent to preserve and pass these same rules to its successor?

If any answer is materially inadequate, the task is not finished.

## 6. Permanent checkpoint rule

Commit and push meaningful durable checkpoints periodically. Do not wait until the end of a long session.

Good checkpoint moments include:

- one coherent source slice completed;
- a useful architecture/research investigation completed;
- before a dangerous refactor;
- before authoritative CI/Windows verification;
- after a verification failure teaches something;
- after repairing that failure;
- after authoritative verification succeeds;
- after evidence is persisted;
- before beginning another architecture boundary;
- before ending a session;
- when context/usage/time is becoming low.

Do not create meaningless timer-based commits. Create enough durable checkpoints that interruption cannot erase substantial work or knowledge.

For long-running agent/tool sessions, bias toward short-interval, focused checkpoint commits and pushes after each small verified unit. A stream/session cancellation must not be able to erase a long stretch of completed engineering work. The checkpoints must still be coherent and meaningful; this is not permission for empty timer-based commits.

## 7. Automatic preservation mode

If usable context, token/session budget, Work/Codex allowance, execution time, or tool availability becomes dangerously low:

1. stop expanding scope;
2. safely conclude or suspend the smallest current unit;
3. inspect changes;
4. commit safe work;
5. push it;
6. update repository handoff state;
7. document unfinished work precisely;
8. leave the exact next command/task for the successor.

Spend final resources preserving state, not beginning another refactor.


## 7a. Recurring blocked-task rotation

Recurring/scheduled workers have a bounded retry budget per task, not an unlimited multi-cycle retry loop.

1. Attempt the repository's normal authorized recovery/fallback routes during the current iteration.
2. If the task remains blocked at iteration end, checkpoint all useful work and evidence durably and mark it `DEFERRED-TO-LIVE` (or equivalent) for the user's live/manual agents.
3. On the next scheduled iteration, do not select that same blocked task again unless the user explicitly reassigns it or durable evidence proves the blocker materially cleared.
4. Select a different actionable unowned task and make concrete progress instead.
5. Preserve deferred work exactly; rotation never authorizes deletion, false completion, unsafe integration, weakened verification, or silent loss of ownership/evidence.
6. Managers must detect repeated scheduled selection of an unchanged deferred blocker and redirect the lane.

The purpose is to keep autonomous cycles productive while leaving hard/manual blockers ready for the user's live agents to finish.

## 8. Exact verification rule

Verification belongs to exact inputs.

Preserve, when applicable:

- source SHA;
- relevant dependency/tool fingerprints;
- CI/run ID;
- platform and architecture;
- SDK/toolchain;
- test/analyzer/self-test results;
- artifact hash;
- cache reuse rationale.

If production source changes, previous green evidence does not automatically apply.

Never manually promote verification state.
Never copy old green booleans to changed fingerprints.

Use explicit language distinguishing:

- implemented;
- inspected;
- compiled;
- locally tested;
- integration tested;
- Windows tested;
- release-gate verified;
- runtime/user tested;
- released.

## 9. Never weaken verification merely to get green

Do not weaken:

- unit tests;
- integration tests;
- fault injection;
- analyzers;
- compiler warnings;
- function verification;
- CI;
- handoff checks;
- safety gates

merely because new work fails them.

Fix the defect. If a check is demonstrably wrong, preserve evidence and rationale before changing it.

A verifier or test-harness change must not prove itself solely by passing after its own modification. Use independent sanity checks or negative fixtures when practical.

## 10. Preserve useful failures

Failures are part of project knowledge.

Record meaningful failed attempts with:

- commit/run;
- symptom;
- root cause;
- fix;
- superseding evidence;
- why the failure matters.

Do not rewrite history merely to make development look cleaner. Preserve useful lessons.

## 11. One architectural verification boundary at a time

Prefer:

> inspect → design → implement one seam → test → commit/push → verify → close → next seam

Do not begin several unrelated refactors merely because context remains.
Do not opportunistically widen scope.
Record unrelated opportunities under next steps.

## 12. Architecture must follow real ownership boundaries

Do not extract code merely because a class/file is large.

Before moving responsibility, identify:

- data ownership;
- mutation ownership;
- transaction boundaries;
- filesystem boundaries;
- lifetime boundaries;
- application coordination;
- failure handling;
- UI coordination;
- dependency direction.

Avoid abstractions that only add indirection.
A cleaner-looking architecture is not automatically a safer architecture.

## 13. Permanent MHW safety invariants

Unless intentionally redesigned with explicit justification, preserve:

- deployment journal/recovery;
- CAS/blob integrity;
- rollback guarantees;
- TOCTOU validation;
- safe path handling;
- archive traversal/device/ADS/reparse protections;
- `ReplaceFileW` / safe existing-file replacement;
- immutable source-library semantics;
- deterministic planning;
- conservative conflict handling;
- explicit user-rule precedence;
- accurate composition versus true-merging semantics;
- SQLite transaction atomicity;
- game-switch restart/service-reconstruction behavior;
- planner-backed Explain Why semantics;
- verification-cache fingerprint integrity.

Any intentional change requires documentation of:

- old behavior;
- new behavior;
- why;
- risk;
- migration effect;
- tests/evidence.

## 14. SQLite transaction boundaries outrank code organization

Never split an atomic operation merely to make `ManagerDatabase` or repository layout prettier.

Before moving database writes, map:

- connection ownership;
- transaction ownership;
- participating statements;
- rollback/recovery path;
- failure semantics.

Keep one logical atomic operation inside one equivalent transaction boundary.

If a future repository participates inside a caller-owned transaction, it must accept/use the shared connection/transaction context instead of silently opening and committing its own independent transaction.

Prefer read-only extraction seams first.

## 15. User data safety outranks developer convenience

Never casually tell users to delete:

- Mods;
- State;
- databases;
- game content;
- saves

to recover from a development mistake.

Prefer safe migration/recovery.

Migration logic must consider interruption, retry, idempotency, partial completion, rollback, and existing supported versions.

## 16. Live-tree mutations remain centralized

Do not introduce random code paths that directly manipulate the live game tree outside the intended deployment/recovery abstractions.

Preserve controlled mutation and immutable-source assumptions.

## 17. Heuristics remain evidence-based

Do not turn weak heuristic evidence into fake certainty.

Conflict/family/provider decisions should preserve, where supported:

- confidence;
- provenance;
- evidence;
- explicit user overrides;
- Explain Why semantics.

Unknown structural incompatibilities should fail closed rather than being guessed away.

## 18. Tests preserve why a bug was fixed

When fixing an actual defect, add a regression test or guard where practical that would detect recurrence.

Do not add tests merely to increase test counts.

Safety-sensitive failures should receive failure-path/fault-injection coverage when practical.

## 19. Preserve performance characteristics

For planner/scanner/hash/conflict/catalog/large-list changes, consider realistic scale and complexity.

Do not casually introduce avoidable:

- O(paths²);
- O(mods²);
- repeated full-tree scans;
- unbatched UI updates;
- unnecessary hashing;
- repeated database round trips.

Use existing benchmarks/large fixtures where relevant.

## 20. Keep heavy work off the WPF Dispatcher

Do not move expensive:

- filesystem work;
- archive extraction;
- hashing;
- conflict planning;
- database processing;
- Nexus activity;
- diagnostics

onto the UI thread for architectural convenience.

Preserve asynchronous/background execution and batched UI publication.

## 21. Preserve public/UI contracts during behavior-preserving refactors

Unless explicitly in scope, preserve:

- XAML bindings;
- command names;
- observable collection contracts;
- public view-model contracts;
- application lifetime semantics.

Add seam/binding regression guards where appropriate.

## 22. Research uncertain platform behavior

Do not guess about safety-sensitive semantics that can be verified.

When necessary consult primary/authoritative documentation for:

- Windows;
- WPF;
- .NET;
- SQLite;
- filesystem APIs;
- NTFS;
- GitHub Actions;
- archive handling;
- external APIs.

Record material findings and implementation consequences in the repository.

## 23. Treat build/CI/dependencies as supply-chain-sensitive

Treat changes to dependencies, package versions, GitHub Actions, build scripts, publishing, external tools, and secrets as security-sensitive.

Prefer:

- pinned dependencies;
- immutable GitHub Action commit SHAs where practical;
- least-privilege workflow permissions;
- minimal new dependencies;
- explicit justification for dependency additions;
- no credentials/secrets in source/logs/artifacts.

Workflow hardening should be its own clearly scoped checkpoint when appropriate.

## 24. Verification infrastructure is production infrastructure

Changes to:

- function verifier;
- stage-cache logic;
- test harness;
- CI workflow;
- packaging scripts;
- continuity scripts

change what “green” means.

Treat them as consequential.
Test them.
Do not hide them inside unrelated refactors.

## 25. Anything expensive to rediscover must be committed

If significant effort discovers:

- architecture boundaries;
- hidden coupling;
- transaction relationships;
- root causes;
- Windows quirks;
- toolchain behavior;
- debugging procedures;
- performance traps;
- failed approaches;
- safety hazards

record that knowledge in the repository.

## 26. Append-only Learned Rules system

`_AGENT_CONTEXT/LEARNED_RULES.md` is the canonical append-only ledger for agent-discovered durable rules.

At the end of every substantial task ask:

> Did I discover a durable rule that would prevent future mistakes or expensive rediscovery?

Add a Learned Rule only when it is:

1. supported by a concrete incident/discovery;
2. likely to recur;
3. meaningful to correctness, safety, or time;
4. actionable;
5. not already adequately covered;
6. stable rather than session-specific;
7. compatible with all Core Rules.

Each entry records:

- Rule ID;
- status: Active or Superseded;
- date;
- scope;
- rule;
- trigger/evidence;
- rationale;
- enforcement;
- relevant commit/run where useful;
- supersedes/superseded-by links where applicable.

Do not silently erase old rules.

When a rule becomes obsolete:

- add the replacement;
- mark the old one Superseded;
- link them;
- preserve history.

Every future agent may add justified Learned Rules.
No agent may weaken a Core Rule without explicit user authorization.

If a Core Rule appears obsolete:

1. document the conflict;
2. collect evidence;
3. propose an amendment;
4. leave it for explicit user approval.

Periodically identify duplicates, consolidate active summaries, supersede outdated Learned Rules, and move detailed rationale to architecture/research docs while preserving history.

Keep `AGENTS.md` concise.

## 27. Every handoff explicitly propagates the chain

Every `NEXT-AGENT-START-HERE.md` update must contain or point unambiguously to language equivalent to:

> You inherit the repository's permanent continuity constitution. Read it before modifying the project. Preserve it and obey active Learned Rules. Add justified Learned Rules from your work. Before finishing, update the repository handoff and explicitly require your successor to inherit, preserve, and recursively propagate these same rules. That successor must repeat the requirement again for the agent after them. Do not break the chain.

This must happen every time, not only once.

## 28. Self-propagation must itself be tested

The handoff validator must fail if a future agent accidentally removes the propagation mechanism.

The test should not depend on exact wording. It should verify the invariant:

> A fresh future agent is still instructed to inherit, preserve, extend when justified, and recursively propagate the permanent rule system.

## 29. Final-agent completion rule

An agent is not done merely when code compiles.

Before finishing it must leave:

- safe repository state;
- meaningful work committed;
- meaningful checkpoints pushed;
- accurate verification state;
- updated continuity context;
- expensive discoveries preserved;
- failures/root causes preserved;
- exact next step;
- clean or explicitly explained working tree;
- recursive propagation intact.

Then it must explicitly tell the next agent to do the same.

## 30. This constitution survives every future handoff

This is not a one-session request.

Agent N must preserve it for Agent N+1.
Agent N+1 must preserve it for Agent N+2.
Agent N+2 must preserve it for Agent N+3.
And so on indefinitely.

No future chat prompt should be required for normal propagation.

If a future agent notices the chain is broken or incomplete, repairing the continuity chain becomes part of that agent's task before handoff.

The repository should remember what conversations forget.

**Do not break the chain.**

## 31. Visible progress is part of the definition of done

Every completed meaningful repository change set must update the root `README.md` with a concise versioned progress summary, advance the patch component in `VERSION.txt`, synchronize canonical version surfaces such as `Directory.Build.props` / `CHANGELOG.md` / continuity metadata, and preserve enough detail for the user and successor to understand what advanced.

One integrated meaningful change set receives one patch increment. Verification/evidence-only persistence, generated verification caches, and release publication/mirroring that only attest the same already-versioned change set stay on that patch to avoid recursive self-bumping. An independent source, configuration, test, documentation, governance, automation, UX, workflow, or behavior change starts a new change set and must advance the patch again.

Managers, reviewers, integration workers, recovery workers, and successors must treat a missing README progress entry, missing patch increment, or inconsistent canonical version identity as incomplete work.

This is a Core Rule. Only explicit user authorization may weaken or remove it.

## 32. Release-ready means release immediately

For every user-facing version, successful completion of the required release gates creates an immediate publication obligation.

- Once the version is integrated into canonical `main` and its required release evidence is green, publish the release in the same execution cycle.
- Do not defer an eligible release for a separate approval prompt, reminder, batching window, later shift, or successor unless the user explicitly orders a hold or delay for that release.
- “Implemented,” “verified,” or “release-ready” is not “done.” A release-owning task reaches `DONE`/`SHIPPED` only after immutable private publication, required public mirroring, and post-publication verification succeed.
- If publication fails, use authorized repair/retry paths immediately and preserve the exact blocker/evidence. A green but unpublished version remains active unfinished work.
- Every handoff and manager must surface any release-ready-but-unpublished version as highest-priority closure work until it is published or explicitly held by the user.

This is a Core Rule. Only explicit user authorization may weaken or remove it.
