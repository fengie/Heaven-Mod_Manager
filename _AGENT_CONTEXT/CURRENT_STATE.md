# v8.8.80 updater E2E evidence persistence — canonical state

v8.8.79 source `6a10008fdcaae5112653593d62b41f5ef4668b36` passed hosted-Windows verification run `37085548752` and installed-client updater E2E run `37085941626`. The E2E update and injected rollback both passed, but durable repository evidence persistence skipped because the exact-source checkout remained shallow and could not prove the later hosted-evidence commit `dc114b07935ca44ba19af82ae5c77a3ead09f4db` descended from the tested source.

## Behavior

- Installed-client E2E persistence detects a shallow checkout and fetches complete canonical-main history before ancestry classification.
- The persistence path explicitly refreshes `refs/remotes/origin/main` so the drift decision cannot reuse a stale remote-tracking ref.
- Release-policy regression coverage requires complete-history recovery to occur before `git merge-base --is-ancestor`.
- Uploaded E2E artifact names use `MHW_E2E_SOURCE_SHA`, preventing the later evidence-only workflow commit from being mislabeled as the tested updater source.
- Divergent or release-relevant main drift remains fail-closed; only evidence/cache-only advancement may receive the E2E closure commit.

## Verification boundary

Current hosted-Windows closure: v8.8.80 source `6e97751252ce1875550a6cd35630bb63e25a1de3` passed run `37091006888` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.80-heaven-windows-closure.log`.

The tested source remains `6e97751252ce1875550a6cd35630bb63e25a1de3` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Remaining independent work

- #559/#558 retain Browse Mods UX and scale work.
- #350/#354 retain external signing/repository-administration prerequisites.
- RECOVERY-005/RECOVERY-007 representative installed Windows acceptance remains independent.
- GitHub Actions artifact storage quota was exhausted during v8.8.79 E2E artifact upload; durable repository evidence is the required fallback and #622 repairs that fallback.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, preserve exact-input verification and release-safety boundaries, and recursively propagate the continuity obligation.
