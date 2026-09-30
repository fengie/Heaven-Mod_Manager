# Agent Control Heaven relay execution fix — 2026-09-30

## Scope
This handoff covers the v8.8.28 follow-up to the already-integrated v8.8.26 registry-retirement and v8.8.27 card-inspection fixes.

## Root cause
`resolveHeavenRelayDir()` already supported the documented per-user checkout `%USERPROFILE%\\HeavenBridgeRepo` and health/inspection used that resolver. Execution did not: both `submitHeavenBridgeJob()` and `waitForHeavenBridgeResult()` defaulted directly to `process.env.AGENT_CONTROL_HEAVEN_RELAY_DIR`. That allowed a false operator picture where the provider could look configured/healthy but delegated work failed before queue publication.

## Implemented
- Submit and wait now default through `resolveHeavenRelayDir()`.
- Error wording describes the required dedicated checkout rather than implying that the env variable is mandatory.
- Regression coverage requires submit/wait to reuse automatic discovery and rejects the old direct-env default.
- Repository version is v8.8.28; Agent Control/plugin identity is v0.6.7.
- Existing retry-exhausted retirement/tombstone and card-inspection behavior is intentionally untouched.

## Verification still required
1. `cd tools/agent-control && npm run check && npm test` on exact candidate SHA.
2. On heaven2, leave `AGENT_CONTROL_HEAVEN_RELAY_DIR` unset with `%USERPROFILE%\\HeavenBridgeRepo` present.
3. Start/reload exact Agent Control candidate.
4. Dispatch one Heaven job and prove queue publication, heaven1 execution, and authoritative result return.
5. Re-smoke card inspection and retry-exhausted retirement.
6. Record exact SHA/run evidence in continuity metadata.

## Remaining risks
If live dispatch still fails after this source fix, next checks are relay checkout cleanliness/branch identity, HMAC key availability, heartbeat freshness, worker startup, and result signature/host/action validation. Do not solve those by duplicating relay paths or weakening authentication.

## Successor guidance
Keep transport resolution centralized. Any future provider health path must share the same configuration resolver used by submit/cancel/wait. Preserve historical failures in durable ledgers/tombstones, not in live registries.
