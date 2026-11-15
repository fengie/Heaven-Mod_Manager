# v8.8.103 mod-boundary importer — canonical release handoff

Canonical repository: `fengie/Heaven-Mod_Manager@main`. Legacy signed updater/product identity is intentionally unchanged. Trainer: `fengie/heaven-toolbox@main`.

## Integrated source

PR #796 merged as `03219436e4f110dfa9b63a66452d94a2bb42553e`. Its exact PR head `7896195b6e02581171b7395d487b33fbdc16c289` passed the native Windows feature gate (26/26; 342 core, 112 automation, 453 integration). Features: mixed-folder and archive mod-boundary discovery, per-game Inbox routing, original preservation, one-shot folder import and safe unknown/FOMOD review. The previous immutable public product release is **v8.8.102**, `updater-main-499`, source `4a26a12ea4c816e54ea3919f3403eaf9b3dd58e4`.

## Verification boundary

Current hosted-Windows closure: v8.8.103 source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002` passed run `43655383641` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.103-heaven-windows-closure.log`.

The tested source remains `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Unresolved risks and issue #791 remaining

Real RAR/7z fixture coverage (solid RAR when supported), per-candidate preview and manual assignment of ambiguous mods, and packaged WPF keyboard/min-width/cancellation acceptance. Anonymous flattened loose files cannot always be decomposed reliably: retain review rather than inventing mod boundaries.

## Successor rules

Bootstrap current Toolbox and this repo, read `AGENTS.md`, current revision/plan, the **entire** `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, and task-relevant `LEARNED_RULES.md`. Every successor must preserve, obey and recursively propagate these Core Rules to the next successor. Do not break the chain. Preserve exact verified source/run/release artifacts and private user Mods/State/downloads; never put secrets or raw user-profile data in public evidence.
