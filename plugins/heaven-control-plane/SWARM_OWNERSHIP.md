# Heaven Control Plane — Live Ownership

This table records the first production vertical slice ownership. It is intentionally narrow so concurrent repository work does not collide.

Exact-main production verification completed on `9da611f41181d462e06ffd02e825114d109e4cd9`: 38 primary Heaven Bridge tests, 10 bridge compatibility tests, 20 Heaven Control Plane tests, and `git diff --check` all passed. The gate cloned remote `main` immediately before execution.

| Task | Owner | Boundary | Dependency | Branch/worktree | Verification | Integration | Blocker |
|---|---|---|---|---|---|---|---|
| HCP-001 protocol + registry | implementation swarm | `protocol.py`, `registry.py`, manifest | existing bridge v2 contract | direct current-main reconciliation | passed on exact-main gate `9da611f` | complete on remote `main` | none |
| HCP-002 bridge compatibility adapter | implementation swarm | `adapters/heaven_bridge.py` | HCP-001 + `heaven-bridge/worker.py` behavior | direct current-main reconciliation | bridge mapping + regressions passed | complete on remote `main` | none |
| HCP-003 execution/filesystem/git slice | implementation swarm | adapter + service dispatch | HCP-001/HCP-002 | direct current-main reconciliation | structured execution/filesystem/Git tests passed | complete on remote `main` | none |
| HCP-004 logs/artifacts | implementation swarm | `observability.py` | HCP-001 | direct current-main reconciliation | pagination + concurrent audit tests passed | complete on remote `main` | none |
| HCP-005 adversarial verification | implementation swarm | `tests/`, `verify.py` | HCP-001..004 | direct current-main reconciliation | malformed/oversized/timeout/traversal/secret/output-bounds tests passed | complete on remote `main` | none |

Future Phase 1 work should claim non-overlapping files/directories before editing shared contracts. The existing `heaven-bridge/` tree remains the implementation/runtime owner for proven primitives; new capabilities should continue to adapt or compose it rather than fork it.
