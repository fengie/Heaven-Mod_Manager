# v8.8.54 collision-safe agent ownership — integration candidate

Canonical baseline includes v8.8.53 Agent Work Reports plus the subsequent updater installed-client evidence. This candidate changes only Agent Control ownership enforcement, its tests, synchronized runtime/plugin identity, and current release/continuity metadata.

Mutable-boundary identity is now case-insensitive during workflow preflight and routing-manifest validation. A routing board cannot claim one mutable boundary twice, and direct counted deploy refuses one shared explicit boundary instead of fabricating numbered subleases. Existing disjoint lanes continue to use separate leases and dependencies.

Exact source validation on the reconciled code slice passed Agent Control syntax checks, 77/77 targeted control-core/server-safety tests, and 279/279 full Agent Control tests on heaven. Because heaven2 remote execution is currently HMAC-auth-blocked, live controller routing mutation/smoke was not bypassed and remains an operator/runtime verification gap.

---

# v8.8.53 Agent Work Reports — candidate

PR #540 is reconciled onto canonical v8.8.52 main `9a38fcbebea605d8be0842053652c0ed54cf9415` with no overlapping file edits from the intervening #539 integration. The candidate adds a standalone loopback reporting dashboard over authoritative Agent Control snapshots plus explicit bounded progress checkpoints. It does not mutate Agent Control lifecycle state.

Local plugin verification on the implementation slice reported Node syntax success, 7/7 deterministic tests, and HTTP smoke. Those historical checks do not transfer to the reconciled/versioned head; fresh exact-head applicable PR gates and a live heaven2 dashboard smoke remain required before integration.

---

# v8.8.52 compact Agent Manager overview — candidate

Reconciled from current v8.8.51 main without importing stale branch metadata. The candidate preserves the shortcut-icon fix and adds only the Agent Manager compact overview, Inspector auto-open behavior, severity summaries, bounded transform/opacity motion, and opt-in UI sound contract from recovered PR #539. Agent Control/plugin identity is 0.6.24.

Exact-head CI and live heaven2 dashboard acceptance remain pending. Other unique branches stay preserved for later semantic recovery.

---

# v8.8.51 shortcut icon integrity — candidate

Canonical baseline is v8.8.50 main `3e4ae104`. The user-reported Windows desktop shortcut corruption was traced to a malformed 48×48 PNG frame inside the committed application ICO: 16/24/32 frames validate, while the 48 frame has an invalid IDAT CRC and malformed termination. The candidate removes only that corrupt frame, preserves the existing icon artwork, and adds deterministic ICO/PNG integrity regression coverage. Windows will scale the intact 32px frame rather than decode corrupt bytes.

Exact-head CI, packaged executable/resource verification, and a freshly recreated Windows desktop shortcut smoke are required before closure. Draft Agent Manager PR #539 remains independently owned; if this urgent bug fix reaches main first, that lane must reconcile/re-version rather than overwrite v8.8.51.

---
# Current state — v8.8.50 candidate

Canonical main is `be4615f` at v8.8.49 with the ComboBox contrast and Mods empty-overlay fixes merged after all three exact-head PR gates passed. PR #525 is closed as superseded; PR #528 preserves its repaired work and reconciliation ancestry while carrying the newer governance integration.

Candidate v8.8.50 is governance/prompt/bootstrap validation only: compact mandatory core, full continuity constitution at startup, indexed task-relevant context, explicit authorized fallback/blocker rules, bug-prevention closure, recursive successor propagation, strict raw UTF-8 byte budgets, adversarial validator fixtures, and synchronized Agent Control contracts. Exact-head v8.8.50 verification is pending; no historical green is being promoted.

Open long-term work remains issues #411/#350/#354 (security/signing/provider boundaries) and #281 (catalog). Unique registry/catalog branches remain preserved for semantic review. Actual operator visual confirmation of the merged v8.8.49 UI fixes is still a runtime verification gap. No new product features until the current queue is reconciled.
