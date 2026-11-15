# v8.8.104 Heaven.gg read-only integration — canonical state

The implementation is integrated in protected source `main@a4ad8123c2288f7b4c820d992e2f4a3e61a769dc` via PR #800. The public desktop game guide is **opt-in**. On explicit user action it validates Heaven.gg's fixed-origin public game registry and opens the appropriate research page in an external browser using existing traced process launch. It cannot register installed games, import mods, change game files, alter updater state or move private data to the website.

The previous immutable public version **v8.8.103** (`updater-main-501`) was published November 14, 2026, source `2b8e5ae302dfcbbefe7b29193b9c96edd79b7002`. It is preserved. **v8.8.104 publication is pending** exact-main Windows acceptance; no new public package is claimed here.

## Verification boundary

Source PR #800, exact head `4b0955a7b110a523a4a612c142e0949bf8c2a1bb`, passed native [Windows Feature Gate 43668216512](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43668216512): 26/26, 352 core tests, 112 automation tests, 453 integration tests. Protected source merge: `a4ad8123c2288f7b4c820d992e2f4a3e61a769dc`. **This is not exact-main v8.8.104 Windows Release Gate proof, publication, installed-client update/rollback, or Heaven2 running identity.** The last completed versioned release receipt remains v8.8.103, [Windows Release Gate 43655383641](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43655383641) and its immutable build 501. The current v8.8.104 source requires its own complete release-gate and immutable artifact/hash checks.

## Independent open acceptance

- **#793:** the last real updater E2E [run 43656163326](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43656163326) stopped before update/rollback because the shared Windows updater root was occupied. The subsequent green [run 43660274094](https://github.com/fengie/Heaven-Mod_Manager/actions/runs/43660274094) only classified publication; its actual updater job skipped. Require a separately owned empty Windows test account/profile, verified old→new positive updater and rollback, retained unrelated sentinels, and independent actual Heaven2 source/health. Never wipe shared user LocalApplicationData or bypass the guard.
- **#791:** real RAR/7z solid fixture acceptance, per-candidate manual registered-game assignment and packaged WPF keyboard/min-width/cancellation checks remain open.
- The public Heaven.gg website [run 43667045385](https://github.com/fengie/Heaven.gg/actions/runs/43667045385) passed real Google Chrome production interactions, but does not prove installed native Mod Manager interaction.

Every successor must inherit, preserve, obey and recursively propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and these Core Rules to the next successor. Do not break the chain.
