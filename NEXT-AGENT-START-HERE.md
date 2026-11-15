# v8.8.104 Heaven.gg game guide — canonical release handoff

Canonical repository: `fengie/Heaven-Mod_Manager@main`; global trainer: `fengie/heaven-toolbox@main`. The previous immutable `updater-main-501` belongs to v8.8.103 source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002` and must not be overwritten.

## Integrated source

PR #800 [read-only Heaven.gg game guide navigation](https://github.com/fengie/Heaven-Mod_Manager/pull/800) merged on main as `a4ad8123c2288f7b4c820d992e2f4a3e61a769dc`. It uses a bounded public `games.json` reader and an explicitly requested browser launch via `ProcessDebug`. Unknown/public catalog data cannot mutate registered games, mod libraries or updater state. Website production Chrome acceptance passed independently on [Heaven.gg run 43667045385](https://github.com/fengie/Heaven.gg/actions/runs/43667045385).

## Verification boundary

Current hosted-Windows closure: v8.8.104 source `20b8fbb67f3ffe803026590cc77e5086d97b2db9` passed run `43675746941` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.104-heaven-windows-closure.log`.

The tested source remains `20b8fbb67f3ffe803026590cc77e5086d97b2db9` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risk and next action

Verify this serialized version PR's exact head/base/native checks, integrate only after authorized gates, inspect the exact-main Windows release run, immutable tag/source and artifact/manifest hashes, then promote allowlisted release evidence via an ordinary protected PR. Do not force publication or adopt a green classification-only updater workflow as an actual update/rollback success. Issue #793 still requires a separately owned empty Windows test profile for real positive updater/rollback and independent Heaven2 installed/health proof. Issue #791 retains real RAR/7z/solid archive, ambiguous-child manual assignment and packaged WPF interaction cases. No private user Mods/State/downloads or historical product/update identity may be mutated as part of this metadata release.

## Successor obligation

Bootstrap current Toolbox and this repo, read `AGENTS.md`, `_AGENT_CONTEXT/CURRENT_REVISION.json`, `_AGENT_CONTEXT/PROJECT_PLAN.md`, the **entire** `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Every successor must inherit, preserve, obey and recursively propagate these Core Rules to the next successor. Do not break the chain.
