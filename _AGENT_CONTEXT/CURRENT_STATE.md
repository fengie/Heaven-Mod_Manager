# v8.8.102 automatic game intake and in-place session — canonical state

This is the canonical state record of the merged v8.8.102 feature source, not a claim that a v8.8.102 packaged binary has been published or installed. Both feature PRs and the red-to-green regression PR have merged into protected `main`.

## Behavior

- PR #789 enables bounded designated-folder ZIP/RAR/7z game categorization, original-source preservation, confident per-game Inbox routing, and review of unknown/contradictory packages. Auto Populate first ingests pending active-game Inbox mods. Wrapping filters and visible Select All/Deselect All replace the sideways Mods toolbar.
- PR #790 uses one current-user Windows session instance, secure bounded activate IPC, and keeps one WPF window/process while switching games. Candidate game registration/relinking does not change the saved active selection before successful session initialization, with regression tests.
- PR #792 introduced four source-contract probes that deliberately failed on the v8.8.101 baseline, then passed on the combined v8.8.102 feature branch. The independent universal archive *pack decomposition* request remains issue #791; one archive containing multiple distinct mods is not yet automatically split.
- Preserve game-scoped and legacy Mods/State/downloads, SQLite/journal integrity, live-file deployment guards, updater signature/trust behavior, and Toolbox-owned shared infrastructure.

## Verification boundary

Current hosted-Windows closure: v8.8.102 source `4a26a12ea4c816e54ea3919f3403eaf9b3dd58e4` passed run `42465970556` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.102-heaven-windows-closure.log`.

The tested source remains `4a26a12ea4c816e54ea3919f3403eaf9b3dd58e4` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Next required action

Merge the narrow version-coordination repair only after exact-head native PR checks. Then run the canonical Windows Release Gate against protected main and verify its full exit code, source SHA, 26/26 stages, package and manifest hashes, and immutable release publication. Promote only genuinely valid allowlisted verification evidence using ordinary protected PR admission. Obtain a separately authenticated installed-client E2E and Heaven2/NieR runtime source/health receipt before declaring physical deployment.

## Unresolved risks and independent work

- Real updater state may belong to another active installation: never clear, replace, or disable checks on ambiguous LocalApplicationData roots. Use independently owned isolated test conditions for issue #793.
- The stored last canonical closed source receipt remains v8.8.99 until current release evidence is promoted. Do not rewrite old evidence to mark new source green.
- Provider breadth #558, installed Auto Populate latency #735, multi-mod archive decomposition #791, and installed runtime UI acceptance remain independent.
- No branch protection, updater signatures, user data, host state, or external permissions were altered by this metadata repair.

Every successor must read the permanent continuity constitution and relevant Learned Rules, preserve these Core Rules, and explicitly require the next agent to inherit and recursively propagate them to its successor.
