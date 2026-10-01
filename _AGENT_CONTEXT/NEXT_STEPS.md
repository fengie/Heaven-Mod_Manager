# v8.8.59 MHW product recovery — ordered next actions

1. Run exact-head required gates for `feat/settings-v8.8.59-20261001`, including strict build/analyzers, integration tests, product-security, ownership, and continuity verification.
2. Re-read current `main` and active PR/branch ownership immediately before integration; merge the Settings candidate only if green and non-conflicting.
3. Persist exact commit/run evidence without recursively bumping the patch, then mark RECOVERY-003 DONE.
4. Reconcile the separate RECOVERY-004 updater/runtime hardening lane onto the newer canonical main without overwriting Settings preference gates or losing its unique hardening semantics.
5. Continue RECOVERY-002 catalog recovery on its existing owner lane.
6. Continue RECOVERY-007 representative installed-game/runtime proof; do not call it DONE until observed.
7. Keep global Agent Control/Heaven/plugin work in `fengie/heaven-toolbox`.

## Verification gap

The v8.8.59 Settings candidate has source/tests/documentation but no exact-head CI evidence yet. The required local Heaven offload route was unavailable in this ChatGPT session, so no local runtime/UI evidence is claimed.

Every successor bootstraps from current `fengie/heaven-toolbox@main` first, then current MHW `main`. Preserve the permanent MHW continuity constitution and active learned rules, and recursively propagate the same obligation. **Do not break the chain.**
