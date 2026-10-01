# v8.8.62 federated catalog browsing — integrated MHW product state

MHW is the **MHW Manual Mod Manager product repository only**. Global reusable training, Agent Control, Heaven Bridge, plugins, shared Git doctrine, and reusable repository policy are owned by `fengie/heaven-toolbox@main`.

Canonical v8.8.62 is integrated on `main` via PR #553 as merge `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71`. The exact PR head `9c2976ae96533b3d439610ef7c770a73d0e14fe3` passed Workflow Feature PR Gate `36869271453`, MHW Product Security Gate `36869271833`, and Heaven Toolbox Ownership Gate `36869271597`. A post-merge compare reports zero file differences from the verified head.

## Current MHW product work

- **RECOVERY-002 / P0:** DONE for the recovered catalog browser/acquisition + CurseForge + permitted-crawler tranche now canonical in v8.8.62.
- **Issue #281:** remains open only for conditional Steam Workshop support, optional Vortex interoperability, and any future provider-specific adapter whose real contract/compliance evidence exists.
- **Issue #350:** source-side independently signed updater design exists; production closure still requires a real external trust anchor/private-key ceremony and a real signed release.
- **Issue #354:** source-side hardening may continue, but repository rulesets remain externally unavailable on the current private-repository/account tier and stable Authenticode identity requires external certificate provisioning.
- **RECOVERY-007 / P0:** discovery source is integrated; representative Windows/runtime installed-game proof remains before DONE.
- **RECOVERY-005 / P1:** dark ComboBox source/tests are integrated; installed Windows/WPF interaction acceptance remains an evidence gap.

## Verification boundary

The closed v8.8.62 source boundary is exact head `9c2976ae96533b3d439610ef7c770a73d0e14fe3`. Its full repository verification passed before merge, including strict solution/App compilation with warnings-as-errors, function verification with zero trace/uncovered-call-site gaps, Core/Automation/Integration tests, focused catalog/UX/XAML/migration/concurrency/workflow regressions, product security, Toolbox ownership, continuity, and `git diff --check`.

Merge `3c19270a9dbf37c434aaa0654fdedcc8c28c6d71` has no file delta from the verified head. Subsequent `[skip ci]` continuity/evidence updates are documentation-only and do not extend the verified production-source boundary.

No external CurseForge credential, live Steam Workshop mapping, Vortex runtime contract, signed-updater production key, Authenticode certificate, or installed-client interaction evidence is implied by the green source gates.

## Coordination

The two older issue #281 recovery branches remain preserved because they still have unique historical commits. They are not canonical implementation lanes; classify their unique ancestry as integrated/superseded/rejected before any deletion.

Global Agent Control/Heaven/plugin work remains owned by `fengie/heaven-toolbox` and must not be recreated in MHW.
