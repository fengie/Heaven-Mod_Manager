# Next steps

## Independent support checkpoint — share-safe support export

The diagnostics/privacy support audit is complete and documentation-only:

- `_AGENT_CONTEXT/DIAGNOSTICS_PRIVACY_AND_SECRET_HANDLING_AUDIT.md`
- active learned rule: LR-005

If this independent lane is selected for production work, do exactly one
checkpoint first: **support-export sanitization + generated-bundle canary
integration tests**.

Keep full-fidelity local diagnostics intact. Sanitize only the shareable export
boundary; default unknown settings/metadata to safe handling; transform or omit
raw JSONL logs; preserve useful non-sensitive relative paths and operation data.
Do not combine this with Nexus transport/provider refactoring, credential-store
migration, filesystem/deployment safety, SQLite transaction changes, or
MainWindow decomposition.

Because production diagnostics code would change, require a fresh exact Windows
Release Gate before closing that future checkpoint. Re-check canonical main and
parallel PRs before beginning it.

This does not reopen or alter the closed PlannerSnapshotRepository evidence.

---


## PlannerSnapshotRepository boundary — CLOSED

Exact verified commit: `efe58f38c4780d40200bcf2b7bbb5914ecd8ebc3`.
Hosted Windows Release Gate: `36336190920`.
Evidence/cache persistence: `852f07b9d6ad0457c161df0aa1c8165981d349cf`.

This requested source boundary is complete. Do **not** immediately start a second storage extraction or use remaining context to broaden scope.

### Exact next action for a successor

1. verify actual canonical `main` and inspect recent history/diffs;
2. inherit `CONTINUITY_PROTOCOL.md` and active `LEARNED_RULES.md` (including LR-001 and LR-002);
3. read the current storage/deep SQLite audits and the rest of `README_FIRST.md`;
4. treat PlannerSnapshotRepository as closed exact evidence, not as permission to decompose ManagerDatabase broadly;
5. obtain/confirm a separately scoped next production boundary before editing;
6. preserve all documented transaction owners, and do not reopen MainWindow page-model splitting merely because the shell is large;
7. before that future task ends, update durable handoff state and explicitly require the next successor to preserve and recursively propagate the same continuity constitution.

Historical failed runs `36335255922` and `36335692754` remain documented in `VERIFICATION.md`; do not erase them.

**Do not break the chain.**

---

## Recursive-continuity governance checkpoint — CLOSED

Exact governance commit `73f1298455ec4c651e211488ececf9803504e60d` passed hosted Windows Release Gate `36333960215` on Windows x64 / .NET SDK 10.0.401. The handoff validator passed, the baseline fixture passed, all four recursive-continuity negative fixtures were rejected, the repository verifier finished 25/25, and the release publish passed. Evidence/cache persistence commit: `6bc50de3f07015b63c58ac6bfba3b7bfce9a104c`.

Every future agent must preserve `CONTINUITY_PROTOCOL.md`, append-only `LEARNED_RULES.md`, the mechanical propagation checks, and must explicitly require its successor to pass the same rules to the agent after them.

The exact next production action is **PlannerSnapshotRepository only**.

**Do not break the chain.**

---


## Immediate: implement one read-only storage boundary

The MainWindow responsibility re-audit is complete. Do **not** continue
splitting MainWindowViewModel at this checkpoint.

Read first:

- `_AGENT_CONTEXT/MAINWINDOW_RESPONSIBILITY_AUDIT.md`
- `_AGENT_CONTEXT/STORAGE_TRANSACTION_BOUNDARY_AUDIT.md`

The remaining WPF responsibilities are not clean passive/page-local seams.
In particular, do not force Mods/ModsView, Conflicts, IssueSuspects or
Overlaps into page models while they still share staged state, badges, counters,
planner analysis, diagnosis, or shell-global busy/status behavior.

### Exact next production action

Extract **PlannerSnapshotRepository only**.

Move the existing `ManagerDatabase.LoadPlannerSnapshotAsync` query assembly
into a read-only repository without changing behavior.

Constraints:

1. preserve full and filtered snapshot results exactly;
2. preserve empty `fileModIds` behavior exactly;
3. preserve current connection semantics on the first move — do not silently
   introduce a new SQLite snapshot/isolation model;
4. do not move `GetModsAsync` in the same checkpoint;
5. do not touch DeploymentExecutor transaction code;
6. wire the new repository through existing composition only as required;
7. add focused snapshot/parity and planner-output regression coverage;
8. source commit -> adjacent continuity commit -> push -> fresh full hosted
   Windows Release Gate -> exact evidence persistence -> closure;
9. do not begin a second storage extraction until that boundary is independently
   green.

## Transaction boundaries that must remain intact

Never fragment one existing connection/transaction across independently
committed repository calls. The audit specifically protects:

- DeploymentExecutor prepared journal:
  operations + operation_journal + original_files;
- DeploymentExecutor final commit:
  deployment_manifest + original_files + mods state + Committed marker;
- DeploymentExecutor rollback commit:
  deployment_manifest + original_files + mods state + RolledBack marker;
- ReplaceModFilesAsync:
  mod_files + blobs + mod_file_armor;
- ChainManualFamilyAsync:
  family membership + mods.family_id + conflict-rule replacement/insertion;
- ProfileRepository.SaveCurrentAsync:
  profiles + complete profile_mods snapshot;
- Unmanaged adoption DB record:
  adoption_runs + adopted_live_files;
- ModTrust launch batch;
- ModIssue suspect batches;
- supersession replacement batch;
- revalidation and enabled/priority batches.

If a future repository participates inside one of these boundaries, it must
accept the caller-owned shared SqliteConnection/SqliteTransaction (or an
equivalent explicit transaction context) instead of opening and committing its
own transaction.

## Closed verification baseline remains unchanged

No production source changed during the MainWindow/storage audit.

- exact verified commit: `106a4569b572473394aa075bcfa5d9c03f2fe44d`
- last production source: `fdbe9b71f29b1c4c7d9fcd061a23c2ca75fa3e34`
- hosted Windows run: `36331057943`
- verifier/fingerprints: **25/25**, **615/615**
- explicit call sites: **6389**, uncovered **0**
- Core **79/79**, Automation **20/20**, Integration **66/66**
- self-test **11/11**
- win-x64 analyzers and ReadyToRun publish: PASS
- release SHA-256: `4872ABDA6D548CB9F97668AF1A3019AC44B146A9A66876F92B065D7009455189`
- evidence/cache persistence: `750a3232ad9ac82bd1587ddd903709b886c9b8bb`

Any PlannerSnapshotRepository source edit starts a new verification boundary;
do not inherit this green state onto changed production code.

## Still out of scope

- FOMOD
- enhanced-game adapter redesign
- Generic Host / DI migration
- MainWindow.xaml decomposition
- broad AppServices redesign
- large ManagerDatabase rewrite
- unrelated product features

## Continuity requirement

Before every handoff: update `_AGENT_CONTEXT`, keep evidence tied to exact
SHAs, run/confirm `scripts/Test-AgentHandoff.ps1`, push stable checkpoints,
and explicitly instruct the next agent to repeat the same incremental
verification/continuity discipline.

**Do not break the chain.**
