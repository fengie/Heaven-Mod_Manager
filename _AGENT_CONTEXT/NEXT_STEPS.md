# v8.8.66 MHW product — ordered next actions

1. Verify the v8.8.66 issue #556 candidate on one exact PR head with Workflow Feature PR Gate, MHW Product Security Gate, and Heaven Toolbox Ownership Gate.
2. Fix any compile/analyzer/UIA failure without weakening the selector acceptance invariant.
3. Refresh canonical main/ownership immediately before integration; merge only after all required gates are green.
4. Publish v8.8.66 through Windows Release Gate from the exact current release-relevant main.
5. Require Updater Installed Client E2E to prove `selectorDisplayText == "Updater E2E Fake Game"`, Switch enabled, Settings enabled, update success, and rollback success.
6. Persist exact release/E2E evidence and close #556 only after the packaged UI acceptance passes.
7. Then continue #558/#559 without regressing the selector contract.

The mandatory local Heaven offload path was attempted but unavailable: the heaven2/heaven bridge health jobs returned no durable status/result/heartbeat. Do not fabricate local verification or weaken bridge authentication.
