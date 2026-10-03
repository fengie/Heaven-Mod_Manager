# v8.8.87 HPN composition precedence — canonical + follow-up handoff

Canonical repository: `fengie/mhw-mods`
Global bootstrap/training: `fengie/heaven-toolbox@main`
Canonical target branch: `main`
Integrated base change: PR #692
Active follow-up: PR #698

## v8.8.87 behavior

- Required literal/manual Main/base packages stay enabled beneath active sibling components.
- Same-source optional/component packages override Main only on overlapping paths.
- Same-Nexus `Ver3.1` / `Ver3.10` / `Ver4.2`-style generations can remain enabled together; the newer generation wins shared paths while older-only files remain active.
- PR #698 makes the observed Nexus 1965 staging deterministic: selecting an HPN 4.x body stages Main plus the newest matching 3.x compatibility generation; it does not auto-enable stale same-major 4.x revisions or equal-version body alternatives.
- Equal-version sibling variants remain a choice unless stronger metadata or an explicit user rule orders them.
- Mod Library advanced rows identify Main / Optional / Revision roles.
- Tests cover Nexus-4678-style `Main → No Bats`, Nexus-1965 `3.1 → 4.2` and `3.10 → 4.2` path precedence, exact Main + newest-3.x + selected-4.x Mod Library staging, whole-family disable, unrelated-page exclusion, and equal-version variant safety.

## Verification boundary

Current hosted-Windows closure: v8.8.87 source `92bb0162e3a64f2bf6b02f7feaeda8ff6b37eb70` passed run `37136719343` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.87-heaven-windows-closure.log`.

The tested source remains `92bb0162e3a64f2bf6b02f7feaeda8ff6b37eb70` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Execution/offload note

This ChatGPT runtime did not expose callable Heaven Local Bridge or Agent Control operations. Implementation therefore used the authenticated GitHub path after live branch/PR overlap checks. No local execution or Windows verification is claimed unless corresponding workflow evidence exists.

## Next action

Observe PR #698 exact-final-head verification. Merge only that green head, verify the resulting remote `main`, then obtain and persist fresh canonical Windows closure because #698 changes product source and tests.

## Unresolved risk

The integrated #692 HPN source is closed by Windows run `37136719343`. PR #698 is a new product/test change and therefore requires its own exact-head feature gate plus a fresh canonical Windows closure after integration. External trust prerequisites and remaining catalog/recovery work stay independent.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, conflict/dependency proof separation, updater/filesystem safety, durable-evidence privacy, and candidate-vs-canonical verification separation. The successor must propagate this continuity obligation to the next agent after them.

**Do not break the chain.** The next agent must preserve and recursively propagate this same continuity obligation to its successor.
