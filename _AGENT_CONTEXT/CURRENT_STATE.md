# v8.8.63 crawler containment — candidate MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

PR #555 is the active v8.8.63 candidate for issue #554. Candidate production source is `76205544ed23422cfb4056dfa88787a162e9a2fd`; later branch commits synchronize continuity metadata only.

## Current MHW product work

- **SECURITY-554 / P0:** ACTIVE. Segment-safe crawler paths, crawler-owned no-auto-redirect transport, bounded/manual redirect validation, encoded-path rejection, and deterministic attempted-request tests are implemented. Exact-head Workflow Feature verification must be green before merge.
- **RECOVERY-002 / P0:** DONE for v8.8.62 catalog browsing/acquisition + CurseForge recovery.
- **Issue #281:** remains open only for conditional Steam Workshop support, optional Vortex interoperability, and future provider-specific adapters with real contracts/compliance evidence.
- **Issue #350:** source-side independently signed updater design exists; production closure still requires a real external trust anchor/private-key ceremony and a real signed release.
- **Issue #354:** source-side hardening is substantially complete; remaining closure requires external repository-admin/publisher evidence.
- **RECOVERY-007 / P0:** discovery source is integrated; representative Windows/runtime installed-game proof remains before DONE.
- **RECOVERY-005 / P1:** dark ComboBox source/tests are integrated; installed Windows/WPF interaction acceptance remains an evidence gap.

## Verification boundary

Do not describe v8.8.63 as integrated until PR #555 has all required gates green on one exact head and merges. Intermediate gate failures were repaired rather than waived. The closed baseline remains v8.8.62 exact source `9c2976ae96533b3d439610ef7c770a73d0e14fe3` merged through PR #553.

No provider-specific HTML crawler, production signing key, Authenticode certificate, repository ruleset, or missing runtime acceptance evidence is implied by source-gate success.

## Coordination

Global Agent Control/Heaven/plugin work remains owned by `fengie/heaven-toolbox` and must not be recreated in MHW. Preserve issue #281 recovery branches until their unique history is classified before deletion.
