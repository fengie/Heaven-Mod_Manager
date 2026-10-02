# v8.8.72 audit reliability hardening — canonical handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Active branch: `main`
Integrated issues: #575, #576, #577
Parent canonical product boundary: `cd189422d1def33d89f2ebaffb30091f2db4ad23`

## Completed predecessor boundary

v8.8.71 PR #573 exact head `90fe998b2024aa43f7e3999185c87a98cac6a016` passed Workflow Feature `36983859719`, MHW Product Security `36983859681`, and Heaven Toolbox Ownership `36983859667`, then squash-merged to main as `cd189422d1def33d89f2ebaffb30091f2db4ad23`. Its GameBanana installed-origin snapshot contract removes duplicate detail hydration while preserving exact identity and fail-closed replacement semantics.

## v8.8.72 canonical state

- Save capture no longer certifies a single permissive read as a coherent recovery snapshot. It takes stable source fingerprints around a private copy, verifies the copied SHA-256, retries bounded mutation, and only then promotes the payload.
- Pre-record save snapshot failure or cancellation cleans the incomplete snapshot directory and cannot create a successful `save_snapshots` row.
- Windows updater publication re-reads the repository `main` ref immediately before its first external publication mutation. A queued stale run sets `publish=false` and cannot expose either public or canonical release assets.
- Active `CURRENT_REVISION.json` describes canonical state, not the branch that produced it. Governance requires `integrationState=canonical-main`, `workingBranch=main`, non-candidate status, and no candidate source commit.
- Deterministic regressions cover concurrent save mutation/retry, continuous instability rejection, cancellation/no-success-row behavior, release freshness placement/gating, and stale continuity negative fixtures.

## Verification provenance

The predecessor v8.8.71 gate evidence above is closed and must not be reused as v8.8.72 authorization. Exact v8.8.72 verification belongs to the integration PR and merge provenance for the final head. Future agents must refresh live checks rather than inferring current verification from this handoff.

## Unresolved risks

- Issue #578 remains live for canonical MHW adapter recovery from a stale generic profile and whole-set handling when multiple profiles share one game root.
- #558 remains open for broader provider-aware catalog pagination/discovery and deterministic scale/performance work.
- #559 remains open for Browse Mods filtering, sorting, provider health, and richer discovery states.
- RECOVERY-007 still needs representative installed Windows/runtime discovery proof.
- Existing external signing/ruleset blockers, artifact-storage constraints, and preserved recovery-branch provenance remain unchanged.

## Ordered continuation

1. Refresh canonical `main`, open issues/PRs, and durable ownership before selecting work.
2. Prefer the oldest actionable unowned issue; do not recreate #575/#576/#577 work after this boundary is canonical.
3. Keep #578 separate from the completed audit-hardening tranche and preserve the v8.8.71 catalog identity/fail-closed invariants.
4. Require fresh exact-head verification for every future integration and read back canonical `main` after merge.
5. Propagate the same continuity obligation onward.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full and retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. The successor **must propagate** this continuity obligation to the next agent after them. **Do not break the chain.**
