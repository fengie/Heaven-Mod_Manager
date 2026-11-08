# v8.8.101 protected release-evidence custody — canonical continuity handoff

Canonical repository: `fengie/Heaven-Mod_Manager` (legacy signed updater/product identity `fengie/mhw-mods` stays unchanged).
Global bootstrap/training: `fengie/heaven-toolbox@main`.
Canonical branch: `main`.
Current product version: **8.8.101**.

## Current behavior

- v8.8.100 #784/#785 empty-NieR startup and game-scoped `Mods by Game/<id>/{Installed,Downloads,Inbox,Archive}` storage are integrated and source-verified. The published updater build 493 contains that source. Legacy mods, State, and user downloads must never be moved/deleted casually.
- v8.8.101 changes release/evidence custody only: Windows Release Gate and installed-client E2E stage their evidence as source-bound, no-force `verification/` branches for normal protected-main PR admission. They do not write directly to main, bypass status checks, or expand workflow token permissions.
- Public Windows closure evidence is an allowlisted passing summary and SHA-256 artifact/report digests, not raw build reports with user-profile paths. Updater installed-client proof remains a one-file allowlisted durable projection.
- Independent #558 catalog breadth and #735 installed Auto Populate performance acceptance remain open.

## Verification boundary

Current hosted-Windows closure: v8.8.101 source `9fa8c50e07c877eb3ae65ae0a34ec3433950d3fb` passed run `42460412791` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.101-heaven-windows-closure.log`.

The tested source remains `9fa8c50e07c877eb3ae65ae0a34ec3433950d3fb` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Next action

1. Refresh live current main, owners, opened PRs, and `_AGENT_CONTEXT/CURRENT_REVISION.json`. Complete exact-head Windows Feature, Security, Ownership, README timestamp, AutoUpdate and updater publication policy gates for the current protected-evidence repair branch before merging.
2. Merge only with the exact tested head and expected main base. Run the canonical main Windows Release Gate for v8.8.101 and independently verify its immutable release tag, versioned Windows ZIP, source SHA, artifact SHA-256, and provenance. Do not force-republish an existing version.
3. Review the generated `verification/windows-v8.8.101-run-<runId>` branch for safe allowed evidence paths and source identity. Create a normal PR and pass native `enforce` before merging proof into protected main. A staged branch is **EVIDENCE_PENDING_PR**, not canonical closure. Never manually promote unverified evidence.
4. Allow the authenticated installed-client E2E workflow to run only after eligible source publication; verify update, rollback, durable allowlisted hashes, and its `verification/installed-e2e-v8.8.101-run-<runId>` evidence PR through normal CI/merge.
5. If the dedicated Windows E2E cannot use an empty owned updater root, fail closed; do not delete another owner's LocalApplicationData or loosen machine security. Obtain actual installed Heaven2/NieR SHA, startup and health proof before declaring physical deployment.

## Unresolved risk

Native CI and GitHub publication are not physical-device deployment. Protected main requires PRs and the updater may contain unrelated live state; missing runtime/PR receipts remain explicit. Preserve exact version/manifest/source identities, filesystem containment, immutable download archives, legacy compatibility, HMAC/Bridge authority, transactional rollback, and independent signing/provenance gates.

## Successor obligation

You inherit the repository's permanent continuity constitution. Read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` in full before modifying the project, obey `_AGENT_CONTEXT/LEARNED_RULES.md`, and bootstrap fresh `fengie/heaven-toolbox@main`. Update justified durable lessons and exact verified source/run/owner handoff before stopping. Every successor **must preserve, obey and recursively propagate** these Core Rules to the agent after them; that successor must repeat the requirement for the next agent. Do not break the chain.
