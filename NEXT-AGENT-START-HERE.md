# v8.8.103 importer — published release and production acceptance handoff

Canonical repository: `fengie/Heaven-Mod_Manager@main`. Trainer: `fengie/heaven-toolbox@main`. Legacy updater identity and user data formats are preserved.

## Integrated and published

PR #796 merged as `03219436e4f110dfa9b63a66452d94a2bb42553e` and adds conservative mixed-folder/archive mod discovery, per-game Inbox routing and one-shot import. PR #797 merged v8.8.103 release metadata as exact source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002`. The public [immutable updater-main-501](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-501) was published November 14, 2026 at 7:01 PM EST. It contains the feature; no duplicate v8.8.103 publication is required.

## Verification boundary

Current hosted-Windows closure: v8.8.103 source `a4ad8123c2288f7b4c820d992e2f4a3e61a769dc` passed run `43668644734` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.103-heaven-windows-closure.log`.

The tested source remains `a4ad8123c2288f7b4c820d992e2f4a3e61a769dc` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and next action

1. **#793:** run old-to-501 update and deterministic rollback under a *separately owned clean Windows test profile*, not the current shared runner profile. Prove both negative occupied-root rejection and positive installed UI/health/journal/sentinels. Preserve exact signed/immutable release source, test custody, hashes and an allowlisted protected evidence PR. No cleanup of another user's updater state, privilege escalation or bypass.
2. **#791:** finish real RAR/7z/solid archive fixtures and error cases, explicit ambiguous-child review/registered-game assignment, packaged WPF UX and cancellation acceptance. Current source is intentionally conservative.
3. Verify installed Heaven2 source SHA, runtime health and rollback independently. No direct host-control path was confirmed from this GitHub-only session.

## Successor obligation

Every successor must bootstrap current Toolbox and this repository, read `AGENTS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, the **entire** `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, active `LEARNED_RULES.md`, and the canonical `_AGENT_CONTEXT/PROJECT_PLAN.md`. Every successor must inherit, preserve, obey and recursively propagate these Core Rules to the next successor. Do not break the chain. Never write secrets or raw user profile paths to public evidence.
