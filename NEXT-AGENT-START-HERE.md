# v8.8.77 authoritative MHW discovery hardening — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issues #602, #603, #604

## v8.8.77 behavior

- Authoritative MHW discovery upgrades a live generic same-root owner only when the resolved executable is exactly `MonsterHunterWorld.exe`.
- Canonical reconstruction preserves profile ID, display name, save path, and explicit store choice while restoring canonical MHW metadata.
- Steam app 582010 identity alone no longer permits new canonical MHW registration through a launcher or unrelated executable.
- Ordinary non-MHW live-profile behavior remains unchanged.

## Verification boundary

v8.8.76 source `afc3ec4f0be8ba36a93b2b880edc6a3cd9de0f52` is closed by hosted Windows verification run `37060606949` (26/26 PASS), persisted at `_AGENT_CONTEXT/EVIDENCE/v8.8.76-heaven-windows-closure.log`.

v8.8.77 changes discovery source, integration regressions, continuity validation, and release metadata. It requires fresh exact-input verification; v8.8.76 evidence is predecessor-only.

## Unresolved risks and next work

- Representative RECOVERY-007 installed Windows/runtime discovery proof remains required beyond deterministic integration coverage.
- #350/#354 retain external signing/repository-administration prerequisites.
- #281 retains only the Steam Workshop applicability tranche.
- #558/#559 and representative RECOVERY-005 acceptance remain independent queues.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and the fail-closed MHW discovery boundary. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
