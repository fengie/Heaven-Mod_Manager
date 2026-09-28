# Profile save transaction atomicity checkpoint — 2026-09-28

## Canonical base and selected scope

- Canonical repository: `fengie/mhw-mods`.
- Canonical `main` inspected at task start: `a8b581176aac0e6bcf09c049285ed40f4b2b392c`.
- Final pre-commit reconciliation base after `main` advanced: `3d24155823b278662cc2aa9ecf9f1bb1a4d7353d` (archive streaming provenance/continuity only; no overlapping profile-save production/test changes).
- Support branch: `agent/support-profile-save-atomicity-20260928`.
- Scope: close the missing fault regression for `ProfileRepository.SaveCurrentAsync` without changing production behavior.
- Production C# changed: **no**.
- Existing profile semantics, SQLite schema, transaction ownership, and UI behavior were deliberately left unchanged.

This was selected because updater publication, frontend UX, and agent-control-plane work already had active owners, while the deep SQLite audit explicitly listed profile-save mid-replacement rollback as missing regression coverage.

## Methodology

Source inspection confirmed that `SaveCurrentAsync`:
1. opens one SQLite connection and transaction;
2. upserts/returns the profile id;
3. deletes existing `profile_mods`;
4. reinserts the current `mods` state with `INSERT ... SELECT`;
5. commits once.

The new regression installs a temporary SQLite `BEFORE INSERT` trigger that raises `ABORT` for one mod during step 4. This forces a real database failure after the existing profile has already been updated and its previous membership deleted inside the transaction.

## Confirmed finding

The rollback contract is confirmed by runtime fault injection on Windows. After the injected insert failure, the test proves that:

- the existing profile id is unchanged;
- `updated_at` is rolled back to its pre-failure value;
- the previous profile membership is restored;
- the previous enabled/priority state is restored;
- the new mod is absent from the saved profile.

This closes missing-regression item 8 in `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md` for constraint/statement failure during profile replacement.

## Exact source/test references

- Production boundary inspected: `src/MhwModManager.Storage/ProfileRepository.cs`, `ProfileRepository.SaveCurrentAsync`.
- Schema inspected: `src/MhwModManager.Storage/Schema.cs`, `profiles` and `profile_mods`.
- New regression: `tests/MhwModManager.AutomationTests/ProfileRepositoryAtomicityTests.cs`.
- Prior durable finding: `_AGENT_CONTEXT/SQLITE_TRANSACTION_ATOMICITY_DEEP_AUDIT.md`.

## Verification actually performed

On `heaven2`, Windows, .NET SDK 10.0.401:

- dependency restore for `MhwModManager.AutomationTests`: PASS;
- focused direct xUnit v3 run for `ProfileRepositoryAtomicityTests`: **1/1 PASS**;
- complete Automation test project using `dotnet test --project ... --no-restore --no-build`: **25/25 PASS**;
- strict Release build of `MhwModManager.AutomationTests.csproj` with `-warnaserror`: **PASS, 0 warnings / 0 errors**;
- `git diff --check`: **PASS**;
- `scripts/Test-AgentHandoff.ps1`: **PASS** (`requiredFiles=80`);
- `scripts/Test-AgentHandoff-NegativeFixtures.ps1`: **PASS**, including rejection of all four broken recursive-continuity fixtures.

An initial positional `dotnet test <project>` command did not execute tests because the repository's `global.json` selects Microsoft.Testing.Platform; using the .NET 10 `dotnet test --project` form executed successfully. That command-shape issue is not a product/test failure.

## Not verified / remaining risk

- No production source changed, so this checkpoint does not claim a new product release boundary or new production fingerprint.
- Cancellation at each individual statement boundary was not deterministically injected; this test covers an actual SQLite statement failure after destructive replacement work has begun.
- Concurrent writers are outside this checkpoint. SQLite transaction atomicity does not by itself define higher-level single-writer policy.
- No claim is made for the separate duplicate-cleanup, launch-observation, snapshot-prune, or migration-status defects from the SQLite audit.

## Learned-rule decision

No new Learned Rule is added. The test validates an already documented transaction-ownership invariant rather than exposing a new reusable root cause. Existing transaction guidance remains sufficient.

## Recommended independent future checkpoint

Take one still-open SQLite audit defect as its own boundary. Do not bundle them. A particularly valuable next checkpoint is D1 duplicate cleanup move-before-delete recovery, but it must be reconciled with LR-007 entity-retirement semantics before production code changes.

## Parallel-agent integration notes

At selection time, active PR ownership was:
- #58 updater post-upload stale-main publication;
- #55 frontend UX/responsive work;
- #47 Heaven agent control plane.

This checkpoint does not touch those production boundaries. It changes one new Automation test file plus this durable support record.

## Successor handoff

Preserve the permanent recursive continuity constitution and active Learned Rules. The successor must re-check canonical `main`, preserve exact verification attribution, and explicitly require the agent after them to inherit and recursively propagate the same continuity system.

**Do not break the chain.**
