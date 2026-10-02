# v8.8.67 MHW product — ordered next actions

1. Run all three required PR gates on the exact v8.8.67 raw-tree UIA candidate.
2. Fix concrete failures only; preserve exact rendered DisplayName and raw-`GameProfile` rejection.
3. Refresh the #556 collaboration claim and fresh main before merge; integrate only an exact green head.
4. Freeze release-relevant main while Windows Release Gate publishes the exact v8.8.67 source.
5. Require Updater Installed Client E2E to pass: exact target resolution, real update, raw-tree selector DisplayName, enabled Switch, enabled Settings, rollback, and workflow evidence verification.
6. If packaged E2E fails, use the newly console-emitted raw UIA/evidence diagnostics for the next repair; do not infer success.
7. Close #556 only after packaged E2E succeeds, then update canonical evidence/state without another product version bump.

Every successor must read and propagate `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and `_AGENT_CONTEXT/LEARNED_RULES.md` onward to the next agent.
