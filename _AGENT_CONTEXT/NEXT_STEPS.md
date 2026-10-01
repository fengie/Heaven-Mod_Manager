# v8.8.57 MHW product-only cutover — ordered next actions

1. Finish exact-head verification for PR #548 and integrate only when all required cutover/product gates are green.
2. Verify canonical MHW `main` contains no `_AGENT_TRAINING/`, `plugins/`, `heaven-bridge/`, `tools/`, or `GLOBAL_GIT_DIRECTIVE.md`, and still requires `fengie/heaven-toolbox@main` bootstrap.
3. Run the canonical Toolbox migration verifier in `--cutover` mode against refreshed MHW/Toolbox main.
4. Keep global Agent Control/Heaven/plugin/history cleanup in `fengie/heaven-toolbox` issue #5; do not recreate those workstreams in MHW.
5. Continue MHW product recovery by live ownership: RECOVERY-007 representative installed-game/runtime proof, RECOVERY-002 catalog, RECOVERY-004 updater/runtime hardening, then RECOVERY-003 and RECOVERY-005.
6. The separately owned updater-hardening branch must refresh post-cutover main and allocate the next patch version rather than overwriting v8.8.57 metadata.

## Product verification gap

RECOVERY-007 is not DONE until representative Windows/runtime installed-game discovery evidence is actually observed. Do not broaden discovery into arbitrary drive crawling or weaken provider isolation merely to obtain proof.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active MHW learned rules. Your successor must propagate this continuity obligation onward. **Do not break the chain.**
