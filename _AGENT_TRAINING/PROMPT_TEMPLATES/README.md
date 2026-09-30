# Prompt Templates

## Global bootstrap requirement

Every standalone or composed role in this directory inherits the same prerequisite: refresh current `main` of `fengie/mhw-mods`, complete its live MHW training/bootstrap gate, then load the target repository's local instructions and state. This requirement applies to work on every repository and must be preserved when templates are copied, composed, scheduled, or delegated.
These are project-agnostic and repository-specific starting points. Replace bracketed placeholders with repository-specific facts. Never treat pasted hashes as more authoritative than current repository truth.

## Agent Manager swarm contract
- `00_SWARM_RULES.txt` is the mandatory shared operating contract for the Agent Manager engineering swarm.
- `01_MANAGER_ORCHESTRATOR.txt` is the manager-specific convergence contract.
- The runtime prompt generator in `tools/agent-control/lib/prompt-templates.mjs` must remain aligned with those contracts so dispatched agents receive the same main-first, collision-safe, evidence-based delivery rules.
- Agent-policy-only changes do not require an application version bump unless they alter shipped application behavior.

## Other templates
Implementation, support/research, review, test, adversarial/stress, integration, recovery, release, and next-best-step templates remain available here.
