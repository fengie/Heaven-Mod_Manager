# v8.8.103 mod-boundary importer — canonical state

Immutable production-download release **updater-main-501** was published November 14, 2026 at 7:01 PM EST from exact source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002`. It contains the integrated PR #796 importer and PR #797 release metadata. Publication is **not** proof of installed-client update or on-device deployment.

## Product and publication

- v8.8.103 includes one-shot folder import, conservative nested ZIP/RAR/7z candidate discovery, game-scoped Inbox routing, replay-safe child receipts and source preservation. FOMOD, ambiguous variants and anonymous loose files remain for review; intake does not modify live game installations.
- Windows Release Gate [run 43655383641](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43655383641) passed 26/26 and produced [immutable release updater-main-501](https://github.com/fengie/Heaven-Mod_Manager/releases/tag/updater-main-501). Exact ZIP SHA-256: `d2beaf650b930c9ac85505301aae94558b34c8883c576ed6f20feac40cdf842c`; manifest SHA-256: `a982c23dd8958d8bc83f00e989094c1895f2d3eb0f05640c647b36d50727bad0`.
- Previous v8.8.102 release `updater-main-499` remains immutable and untouched. The source after protected verification-evidence integration may advance through evidence-only commits; the published product still belongs to its exact source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002`.

## Verification boundary

Current hosted-Windows closure: v8.8.103 source `a4ad8123c2288f7b4c820d992e2f4a3e61a769dc` passed run `43668644734` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.103-heaven-windows-closure.log`.

The tested source remains `a4ad8123c2288f7b4c820d992e2f4a3e61a769dc` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Production acceptance still open

- **#793 (P0):** use a separately owned empty Windows user/profile for real prior-release-to-501 update and rollback; verify old/new SHA, journal, sentinel retention, UI/health and no effects on the real user updater root. A normal runner service account is not an isolated profile.
- **#791 (P1):** real RAR/7z fixture matrix including solid RAR where supported, an explicit per-candidate preview/manual game assignment flow, and packaged WPF keyboard/min-width/cancellation acceptance.
- Do not clear user LocalApplicationData, weaken production updater paths, force-republish the immutable artifact or infer physical Heaven2 installation from GitHub Actions. Every successor must propagate the complete continuity constitution.
