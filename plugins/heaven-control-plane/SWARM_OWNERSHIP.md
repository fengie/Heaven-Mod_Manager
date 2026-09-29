# Heaven Control Plane — Live Ownership

This table tracks active and completed control-plane ownership so concurrent agents do not independently edit the same shared core files.

The original vertical slice was exact-main verified on `9da611f41181d462e06ffd02e825114d109e4cd9`: 38 primary Heaven Bridge tests, 10 bridge compatibility tests, 20 Heaven Control Plane tests, and `git diff --check` all passed. Phase 1 v0.2.0 convergence is being revalidated on a fresh branch from current `main` before integration.

| Task | Owner | Files/directories owned | Dependencies | Branch/worktree | Current commit | Verification state | Integration state | Blocker |
|---|---|---|---|---|---|---|---|---|
| HCP-001 protocol + registry | implementation swarm | `protocol.py`, `registry.py`, manifest | existing bridge v2 contract | canonical `main` | `c2f78b5` | passed exact-main gate `9da611f` | complete on remote `main` | none |
| HCP-002 bridge compatibility adapter | implementation swarm | `adapters/heaven_bridge.py` | HCP-001 + `heaven-bridge/worker.py` behavior | canonical `main` | `c2f78b5` | bridge mapping + regressions passed | complete on remote `main` | none |
| HCP-003 execution/filesystem/git slice | implementation swarm | adapter + service dispatch | HCP-001/HCP-002 | canonical `main` | `c2f78b5` | structured execution/filesystem/Git tests passed | complete on remote `main` | none |
| HCP-004 logs/artifacts | implementation swarm | `observability.py` | HCP-001 | canonical `main` | `c2f78b5` | pagination + concurrent audit tests passed | complete on remote `main` | none |
| HCP-005 adversarial/security verification | implementation swarm | `tests/`, `verify.py`, permission/artifact hardening | HCP-001..004 | canonical `main` + convergence transplant | `2256338` | branch verification pending | pending v0.2.0 convergence | none |
| HCP-006 persistent sessions | Phase 1 sessions/search owner + integration lead | registry + Heaven Bridge adapter + focused tests | verified bridge session primitives + HCP-005 | `integration/hcp-0.2.0-main-20260929` | `2256338` | branch verification pending | pending | none |
| HCP-007 filesystem search | Phase 1 sessions/search owner + integration lead | registry + Heaven Bridge adapter + focused tests | verified bridge `fs_search` primitive + HCP-005 | `integration/hcp-0.2.0-main-20260929` | `2256338` | branch verification pending | pending | none |
| HCP-008 repository indexing | indexing agent | new indexing/search boundary; avoid shared-core edits until contract is reconciled | stable v0.2.0 registry contract | `agent/hcp-indexing-20260929-0746` | branch currently based at `5800919` | active | not integrated | must sync current `main` before delivery |

The existing `heaven-bridge/` tree remains the implementation/runtime owner for proven primitives. New capabilities should adapt or compose it rather than fork it. Shared registry/protocol edits must be reconciled through the active convergence owner before additional Phase 1 modules land.
