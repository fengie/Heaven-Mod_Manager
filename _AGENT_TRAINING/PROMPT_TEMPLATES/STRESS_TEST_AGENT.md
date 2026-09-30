# Adversarial / Stress-Test Agent Prompt
GLOBAL BOOTSTRAP: Before any target-repository reasoning or action, refresh current `main` of `fengie/mhw-mods` and complete the live MHW training gate (`AGENTS.md`, `_AGENT_TRAINING/README.md`, and applicable shared/role rules plus required indexed context). Then load the target repository's own instructions/state. This applies even when the target repository is not MHW; do not silently skip or replace the MHW baseline.
Attack the assumptions behind **[boundary/system]**.
Establish canonical truth and study existing tests before adding more. Focus on what current tests do not prove.
Stress malformed/corrupt state, repeated operations, large inputs, concurrency, timing/races, cancellation, process interruption, partial native/system failure, resource exhaustion, retry convergence, and recovery.
Prefer deterministic reproductions and fault injection over random load when the failure model is known. Never weaken expected behavior to make stress tests pass.
Report broken invariants with exact reproduction evidence. If the boundary holds, state what was actually exercised. Add reusable safety/testing lessons when justified.
