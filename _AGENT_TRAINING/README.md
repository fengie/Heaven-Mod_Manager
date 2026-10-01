# Compatibility notice — global trainer moved

This directory is a **temporary compatibility mirror** during the MHW → Heaven Toolbox cutover. It is no longer the canonical cross-repository programming-agent trainer.

## Canonical source

For every programming/repository assignment, bootstrap from current:

`fengie/heaven-toolbox@main`

Read its:

1. `AGENTS.md`
2. `_AGENT_TRAINING/README.md`
3. `_AGENT_TRAINING/AGENT_OPERATING_STANDARD.md`
4. `GLOBAL_GIT_DIRECTIVE.md`

Then refresh and read the target repository's own project-specific instructions and current state.

For MHW work, this repository remains authoritative only for MHW Manual Mod Manager source, tests, `_AGENT_CONTEXT/`, bugs, plans, release state, evidence, and runtime/project facts.

## Cutover rule

Do not add or evolve generic training here. New reusable doctrine belongs in Heaven Toolbox first. This local directory may only receive temporary compatibility maintenance required to complete the cutover safely.

The final migration state deletes this MHW `_AGENT_TRAINING/` copy entirely after Toolbox-first operation is proven and MHW's ownership regression gate is active.

Higher-priority platform/safety requirements and the user's current explicit instruction always outrank repository guidance.
