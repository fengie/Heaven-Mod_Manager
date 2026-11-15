# v8.8.103 importer — published release and production acceptance handoff

Canonical repository: `fengie/Heaven-Mod_Manager@main`. Trainer: `fengie/heaven-toolbox@main`. Legacy updater identity and user data formats are preserved.

## Integrated and published

PR #796 merged as `03219436e4f110dfa9b63a66452d94a2bb42553e` and adds conservative mixed-folder/archive mod discovery, per-game Inbox routing and one-shot import. PR #797 merged v8.8.103 release metadata as exact source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002`. The public [immutable updater-main-501](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-501) was published November 14, 2026 at 7:01 PM EST. It contains the feature; no duplicate v8.8.103 publication is required.

## Verification boundary

Windows Release Gate [run 43655383641](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43655383641) passed **26/26** for exact main source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002`. Full verification receipt: `_AGENT_CONTEXT/EVIDENCE/v8.8.103-heaven-windows-closure.log`. Immutable ZIP SHA-256 `d2beaf650b930c9ac85505301aae94558b34c8883c576ed6f20feac40cdf842c`, manifest SHA-256 `a982c23dd8958d8bc83f00e989094c1895f2d3eb0f05640c647b36d50727bad0`. Evidence-only PR commits must not be described as new verified product sources.

[Installed Client E2E run 43656163326](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43656163326) **failed before update/rollback**: the runner's shared Windows updater root was nonempty; the safety guard correctly refused deletion. Do not claim on-device update, rollback or Heaven2 runtime verification from this build/publication.

## Unresolved risks and next action

1. **#793:** run old-to-501 update and deterministic rollback under a *separately owned clean Windows test profile*, not the current shared runner profile. Prove both negative occupied-root rejection and positive installed UI/health/journal/sentinels. Preserve exact signed/immutable release source, test custody, hashes and an allowlisted protected evidence PR. No cleanup of another user's updater state, privilege escalation or bypass.
2. **#791:** finish real RAR/7z/solid archive fixtures and error cases, explicit ambiguous-child review/registered-game assignment, packaged WPF UX and cancellation acceptance. Current source is intentionally conservative.
3. Verify installed Heaven2 source SHA, runtime health and rollback independently. No direct host-control path was confirmed from this GitHub-only session.

## Successor obligation

Every successor must bootstrap current Toolbox and this repository, read `AGENTS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, the **entire** `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and the canonical `_AGENT_CONTEXT/PROJECT_PLAN.md`. Every successor must inherit, preserve, obey and recursively propagate these Core Rules to the next successor. Do not break the chain. Never write secrets or raw user profile paths to public evidence.
