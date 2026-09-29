# Manager swarm checkpoint — 2026-09-29

Canonical repository: `fengie/mhw-mods`  
Canonical branch: `main`

## Critical path

Automatic updater publication is closed. The only remaining updater-completion boundary is the disposable installed-client E2E:

1. old→new update to immutable `updater-main-61`, with exact restarted build/source identity, health acknowledgement, and unchanged seeded `Mods`, `State`, and unknown-file hashes;
2. separate deterministic fault-injected rollback proving previous owned bytes/metadata/executable restoration, new-only product-file cleanup, unchanged seeded data, and safe terminal journal/recovery state.

## Live ownership map

| Workstream | Owner | Status | Dependencies | Deliverable |
|---|---|---|---|---|
| Updater hosted verification/publication | GitHub Windows Release Gate / canonical main | DONE | PR #91 | Run 36541891969; immutable updater-main-61 |
| Updater installed-client E2E | Heaven worker job `manager-20260929-updater-e2e-build61-a1` | ACTIVE | updater-main-60/61 artifacts + disposable Windows install | Scenario A/B evidence or exact blocker |
| Updater continuity reconciliation | Top-level manager | DONE | hosted/release evidence | CURRENT_REVISION/CURRENT_STATE/NEXT_STEPS corrected |
| Game-profile ID containment | existing PR #90 owner | HOLD / separate lane | current main + exact candidate CI | no edits/merge into updater E2E lane |
| Support-bundle privacy | existing PR #79 owner | HOLD / separate lane | independent verification | no edits/merge into updater E2E lane |
| Crash-bisector controls | existing PR #76 owner | HOLD / separate lane | independent verification | no edits/merge into updater E2E lane |
| Continuity-validator hardening | existing PR #74 owner | HOLD / separate lane | independent verification | no edits/merge into updater E2E lane |
| Duplicate-cleanup compensation | existing PR #71 owner | HOLD / separate lane | focused/full gates | no edits/merge into updater E2E lane |
| Agent Control v2 | existing PR #59 owner | ACTIVE / separate lane | its own review/gates | do not duplicate or merge into updater E2E lane |

## Exact closed updater evidence

- Source: `5abe40304dfcb48f96e750bd7da3d0075315625b`.
- Hosted Windows Release Gate: `36541891969`, 25/25 PASS.
- Promoted functions: 748/748.
- Updater build: 61.
- ZIP SHA-256: `C31CAA1F5CBA65EBF9D526B02BA718F807EBC18A7E7554269E3754D710F86420`.
- Immutable release: `updater-main-61`, exact source target, exactly ZIP + `update-manifest.json`.
- Manifest SHA-256: `4021E5303263A42255E80B40DA6C9C9349B055FB87BB970C2A71DA149DB4D2BE`.
- Evidence/cache persistence: `4f0e2402d3a61e9ba3db005f026b02ccb4aba7de` (verification/evidence only).

## Management rules for this checkpoint

Do not reopen publication/C12/retry-tag work absent contradictory evidence. Do not merge unrelated support work merely because it is open. Do not let any second worker duplicate the installed-client E2E while the assigned Heaven job is active. If the Heaven E2E is blocked only by credential authority or a machine-specific Windows constraint, record the exact blocker first and escalate to heaven2 only for that minimal validation boundary.
