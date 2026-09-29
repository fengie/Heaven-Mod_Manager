# Heaven Control Plane — Live Ownership

This table records the first production vertical slice ownership. It is intentionally narrow so concurrent repository work does not collide.

| Task | Owner | Boundary | Dependency | Branch/worktree | Verification | Integration | Blocker |
|---|---|---|---|---|---|---|---|
| HCP-001 protocol + registry | current implementation cycle | `protocol.py`, `registry.py`, manifest | existing bridge v2 contract | direct current-main reconciliation | pending exact tests | pending | none |
| HCP-002 bridge compatibility adapter | current implementation cycle | `adapters/heaven_bridge.py` | HCP-001 + `heaven-bridge/worker.py` behavior | direct current-main reconciliation | pending bridge mapping tests | pending | none |
| HCP-003 execution/filesystem/git slice | current implementation cycle | adapter + service dispatch | HCP-001/HCP-002 | direct current-main reconciliation | pending exact tests | pending | none |
| HCP-004 logs/artifacts | current implementation cycle | `observability.py` | HCP-001 | direct current-main reconciliation | pending pagination/concurrency tests | pending | none |
| HCP-005 adversarial verification | current implementation cycle | `tests/`, `verify.py` | HCP-001..004 | direct current-main reconciliation | pending | pending | Codex swarm dispatch capacity is currently blocked; deterministic Heaven Bridge execution remains available |

Future Phase 1 work should claim non-overlapping files/directories before editing shared contracts.
