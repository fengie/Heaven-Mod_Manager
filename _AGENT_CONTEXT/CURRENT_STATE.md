# v8.8.102 automatic game intake and in-place session — canonical state

This is the canonical state record of the merged v8.8.102 feature source, not a claim that a v8.8.102 packaged binary has been published or installed. Both feature PRs and the red-to-green regression PR have merged into protected `main`.

## Behavior

- PR #789 enables bounded designated-folder ZIP/RAR/7z game categorization, original-source preservation, confident per-game Inbox routing, and review of unknown/contradictory packages. Auto Populate first ingests pending active-game Inbox mods. Wrapping filters and visible Select All/Deselect All replace the sideways Mods toolbar.
- PR #790 uses one current-user Windows session instance, secure bounded activate IPC, and keeps one WPF window/process while switching games. Candidate game registration/relinking does not change the saved active selection before successful session initialization, with regression tests.
- PR #792 introduced four source-contract probes that deliberately failed on the v8.8.101 baseline, then passed on the combined v8.8.102 feature branch. The independent universal archive *pack decomposition* request remains issue #791; one archive containing multiple distinct mods is not yet automatically split.
- Preserve game-scoped and legacy Mods/State/downloads, SQLite/journal integrity, live-file deployment guards, updater signature/trust behavior, and Toolbox-owned shared infrastructure.

## Verification boundary

The exact v8.8.102 PR source `8986cd4af6bd5f886d093e1b404b773095387c9a` passed 26/26 Windows verification stages and all associated strict build, security, updater contract, and README gates. It merged into `main` as `190858ad842deff645bcc836be3ab24fe09cc19a`.

**The first exact-main v8.8.102 Windows Release Gate, run `42463658105`, failed governance parity:** `handoff-manifest currentVersion must match VERSION.txt`. The test suite itself was green (432 integration, 342 core and 112 automation passing), but the release verification did not pass and no v8.8.102 publication can be claimed from that run. This metadata repair must be merged and then independently verified again on the resulting main SHA.

The immutable v8.8.101 release `updater-main-495` belongs only to exact source `446e933f778dae8f2da0238951e28b8b84f3f0b0`. Its previous installed-client E2E attempt failed closed because the shared updater state root was nonempty (issue #793). A green classification-only workflow with the actual updater job skipped does not prove successful installed updates or rollbacks.

## Next required action

Merge the narrow version-coordination repair only after exact-head native PR checks. Then run the canonical Windows Release Gate against protected main and verify its full exit code, source SHA, 26/26 stages, package and manifest hashes, and immutable release publication. Promote only genuinely valid allowlisted verification evidence using ordinary protected PR admission. Obtain a separately authenticated installed-client E2E and Heaven2/NieR runtime source/health receipt before declaring physical deployment.

## Unresolved risks and independent work

- Real updater state may belong to another active installation: never clear, replace, or disable checks on ambiguous LocalApplicationData roots. Use independently owned isolated test conditions for issue #793.
- The stored last canonical closed source receipt remains v8.8.99 until current release evidence is promoted. Do not rewrite old evidence to mark new source green.
- Provider breadth #558, installed Auto Populate latency #735, multi-mod archive decomposition #791, and installed runtime UI acceptance remain independent.
- No branch protection, updater signatures, user data, host state, or external permissions were altered by this metadata repair.

Every successor must read the permanent continuity constitution and relevant Learned Rules, preserve these Core Rules, and explicitly require the next agent to inherit and recursively propagate them to its successor.
