# v8.8.78 contextual accessibility and upgrade guidance — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Change set: issue #605 contextual accessibility + upgrade guidance

## v8.8.78 behavior

- Mod Library gallery previews expose contextual UI Automation names derived from the current mod rather than raw cache paths.
- Compact Mod Library thumbnails expose target-specific accessible names.
- Needs attention **Clear mark** buttons expose action+target accessible names while preserving their existing command and `ModId` parameter.
- The Safe upgrade procedure refers to the current release instead of the obsolete v8.5.0 literal while preserving `Mods` and `State`.
- `XamlBindingSafetyTests` guards the image/action automation bindings and upgrade-document wording.

## Verification boundary

- v8.8.77 canonical predecessor head at task start: `51e8693f9d113d2ade6f1f0b2d6c2131bceb78a1`.
- Do not inherit verification from v8.8.76 or earlier for changed v8.8.78 XAML, tests, documentation, or version metadata.
- Require fresh exact-head gates on the final v8.8.78 candidate before integration, then verify canonical `main` contains the intended tree.

## Coordination

- Issue #604 has a separate active owner for `_AGENT_CONTEXT/CURRENT_REVISION.json` and `_AGENT_CONTEXT/CURRENT_STATE.md`; preserve that owner's work and reconcile rather than overwrite it.
- Issues #558/#559/#602/#603 and representative RECOVERY acceptance remain independent queues.
- #350/#354 retain externally gated signing/repository-administration work.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification and release supply-chain rules. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
