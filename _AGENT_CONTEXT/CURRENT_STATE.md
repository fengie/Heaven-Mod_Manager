# v8.8.101 protected release evidence — canonical state

v8.8.101 is the next source/release candidate for repairing the post-publication protected-main evidence failure while retaining the shipped v8.8.100 game-scoped mod library and empty-startup fixes. This is a **canonical source-state record**, not a published binary or installed runtime receipt.

## Behavior

- v8.8.100 #784/#785 fixes remain intact: zero-mod NieR startup does not hash game assets, and new packages/downloads use `Mods by Game/<game-id>/{Installed,Downloads,Inbox,Archive}`, while legacy Mods/State remain indexed without destructive migration.
- Source-verified v8.8.101 release logic stages one exact-run Windows verification evidence commit under an immutable PR branch instead of pushing directly to protected main. The current main ruleset still requires an ordinary PR and successful `enforce` status.
- Public Windows evidence contains a 0-failure verification summary and SHA-256 report/build/artifact digests, not raw runner/user file paths. Failed, ambiguous or source-mismatched proof remains rejected.
- Installed-client E2E retains verified update/rollback and one-file evidence projection but stages that evidence through a PR branch as well. No new workflow permissions, branch-protection bypass, force push, new worker, host restart or user-file deletion.
- Existing catalog policy, provider-safety, transactional deployment/rollback, updater signatures and Toolbox-owned shared infrastructure remain unchanged.

## Verification boundary

Current hosted-Windows closure: v8.8.101 source `9fa8c50e07c877eb3ae65ae0a34ec3433950d3fb` passed run `42460412791` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.101-heaven-windows-closure.log`.

The tested source remains `9fa8c50e07c877eb3ae65ae0a34ec3433950d3fb` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Next required action

Reconcile current exact main and candidate PR head; require all native CI/security/policy/admission gates, then serial expected-head merge. Verify the v8.8.101 canonical Windows release, immutable updater bytes, signed/provenance status and checked artifact SHA. Approve only actual evidence branches after reviewing their source binding and native PR checks; merge exact evidence PRs to main. Require installed-client E2E and a separately authenticated Heaven2/NieR installed source/health check before claiming physical deployment.

## Unresolved risks and independent work

- Windows GitHub Actions token may create release-evidence branches with existing `contents:write`, but it has **not** been proven on this protected repo. A branch push failure must fail closed, not request broader permissions or bypass protection without authorization.
- Real installed-client E2E can be blocked by existing per-user updater state. Preserve that state and use a separately authorized isolated runner if required.
- #558 provider breadth and #735 installed Auto Populate timing remain independently open. Release identity, source checksum, full build provenance and physical installed SHA are separate facts.
- Only confirmed successful main publication and enforced PR evidence may update the canonical closure record. Do not promote a candidate or stale test to a green source by hand.

Every successor must read the permanent continuity constitution and current Learned Rules and explicitly require the next agent to inherit, preserve and recursively propagate these Core Rules to its successor.
