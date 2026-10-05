# v8.8.98 — MHW Manual Mod Manager

Current product version: **8.8.98**.

## Recent patches

Keep this section intentionally short. The README shows the **current patch plus the two immediately preceding patches only**; complete history belongs in [`CHANGELOG.md`](CHANGELOG.md).

## v8.8.98 — exhaustive Browse Mods first-run indexing

- Make **Index All Available** seed page 1 when an enumerable provider has no saved cursor, then follow durable continuations to exhaustion.
- Resume existing cursors without restarting already persisted pages; already-exhausted scopes remain local no-ops.
- Reject repeated cursors and empty continuation pages so malformed providers cannot create infinite indexing loops.
- Keep Nexus coverage honest: this slice does not claim unsupported full-catalog Nexus discovery, and #558 remains open for provider breadth plus end-to-end scale/performance acceptance.

## v8.8.97 — CurseForge Browse Mods pagination

- Reuse the shared paged-provider contract for CurseForge so **Load More** continues through the existing provider-neutral Browse Mods path.
- Persist a validated non-negative search index as the continuation and derive exhaustion from response pagination metadata.
- Reject invalid cursors before transport while preserving the existing CurseForge provider boundaries.
- Add deterministic continuation and exhaustion coverage without introducing provider-specific UI state.
- #558 remains open for remaining provider breadth and end-to-end scale/performance acceptance.

## v8.8.96 — resumable Browse Mods pagination

- Add an optional paged-provider contract while preserving existing non-paged providers and capability-gated remote search behavior.
- Make GameBanana browse pages resumable with opaque page/offset cursors, including mid-page continuation so a UI limit cannot skip remaining provider IDs.
- Persist continuation only after the returned page is fully written; failed continuation preserves the prior cursor so retry cannot jump over failed work.
- Add a serialized **Load More** Browse Mods action plus deterministic continuation, failure-retry, exhausted-no-op, and binding coverage.
- #558 remains open for broader multi-provider scale/performance acceptance beyond this GameBanana continuation tranche.
