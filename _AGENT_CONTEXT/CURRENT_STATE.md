# v8.8.78 Browse Mods actionable empty states — canonical state

v8.8.77 remains the last closed hosted-Windows verification boundary. v8.8.78 advances the Browse Mods UX under issue #559 without changing provider trust, acquisition, or exact-file install safety.

## Behavior

- Browse Mods distinguishes an unpopulated catalog from a non-empty search that returns zero visible results instead of presenting a blank grid.
- Empty catalog state offers **Refresh Providers**; zero-match search state offers **Clear Search** without discarding the query automatically.
- Recovery actions have explicit UI Automation names and remain normal keyboard-reachable WPF buttons.
- The result grid collapses while empty, and the details pane no longer instructs users to select a row when no selectable row exists.
- Selected details are retained only when the same provider/mod identity remains in the visible result set; otherwise the stale selection clears.
- The browse badge reports visible result count rather than labeling filtered matches as total cached content.
- Existing exact-file selection/install gating, provider capability checks, and failure isolation remain unchanged.

## Verification boundary

Current hosted-Windows closure: v8.8.78 source `e6e6a91b40ac281f38d0f11dc6b7a77052ca4071` passed run `37082825628` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.78-heaven-windows-closure.log`.

The tested source remains `e6e6a91b40ac281f38d0f11dc6b7a77052ca4071` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

- #559 remains open for filters, sorting, provider-health presentation, loading/stale/partial-failure states, and broader discovery UX.
- #558 retains provider-aware catalog scaling/performance work.
- #350/#354 retain external signing and repository-administration prerequisites.
- Representative RECOVERY-005/RECOVERY-007 installed Windows acceptance remains independent work.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve exact-input verification and release-safety boundaries, and recursively propagate the continuity obligation.
