# v8.8.71 catalog installed-origin snapshot dedup — candidate state

v8.8.70 is integrated on canonical main as `350752d315ba6db1d329f726d181bad173b41514`. Its exact PR #572 head `c99cf0cd9e3998f2f1ea01f2b6c1612b045b0cb2` passed Workflow Feature `36982837749`, MHW Product Security `36982837913`, and Heaven Toolbox Ownership `36982837948`.

Issue #569 / PR #573 is the current v8.8.71 lane. Issue #578 separately tracks two stale-profile semantic gaps found after v8.8.70 integration.

## Candidate behavior

- `InstalledCatalogOriginChecker` can consume one authoritative provider snapshot containing both mod metadata and exact file rows.
- Providers that do not implement the optional snapshot contract retain the existing `GetModAsync` + `GetModFilesAsync` fallback.
- GameBanana implements the snapshot contract, so one installed-origin update check performs one mod-detail request instead of fetching the same detail payload twice.
- Capability gating, cancellation, provider health/failure classification, exact mod/file identity validation, and fail-closed replacement selection remain unchanged.
- Nexus remains update-unsupported under its current declared capabilities; this patch does not enable or probe an unsupported Nexus update mode.
- Deterministic coverage counts GameBanana detail requests and verifies the exact installed file remains current.

## Verification boundary

The v8.8.69-based reconciled source/test head `51aeff9d0c7da2843949b0f307ac1f4907982aef` passed Workflow Feature `36982514282`, including 324/324 Core unit tests and all focused workflow/catalog regressions. That run is superseded for final integration because v8.8.71 now includes the v8.8.70 canonical parent plus release/continuity metadata.

No v8.8.71 merge authorization is claimed yet. Workflow Feature, MHW Product Security, and Heaven Toolbox Ownership must all pass on the exact final PR #573 head.

Issue #578 and representative RECOVERY-007 Windows/runtime discovery proof remain open after this catalog patch. #558 and #559 remain separate work.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and propagate the continuity obligation onward.
