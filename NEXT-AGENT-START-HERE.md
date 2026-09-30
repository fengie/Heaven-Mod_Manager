# v8.8.50 governance integration — current handoff

Owner: PR #528 / branch `codex/training-governance-integration`, reconciled onto canonical v8.8.49 main `be4615f`. PR #525 is closed as superseded; its repaired head and later reconciliation ancestry are preserved in this branch. New product features remain frozen.

## Current verification and integration truth

v8.8.49 UI fixes are merged on main. Their exact candidate `3bc8f6b` passed Security Supply Chain, Agent Control, and Workflow Feature gates before merge. The actual operator visual confirmation for the ComboBox/Mods fixes remains an explicit runtime gap.

Candidate v8.8.50 consolidates agent governance while preserving mandatory pre-response training, full continuity-constitution startup reading, recursive successor propagation, authorized fallback rules, bug-prevention closure, strict raw UTF-8 byte budgets, and adversarial handoff validation. Historical green does not transfer to this changed candidate.

## Next ordered actions and risks

1. Run exact-head Security, Agent Control, and Workflow Feature gates for PR #528; repair only real failures and do not weaken safeguards.
2. Refresh main/head immediately before merge; merge only the verified exact head, then confirm remote main contains the intended v8.8.50 tree.
3. Retire temporary governance branches only after ancestry/semantic preservation proof. Preserve unique registry/catalog work until separately reviewed.
4. Issues #411, #350, #354, and #281 remain unresolved. Resume paused cached-plugin routing work only after the existing queue is reconciled.

Each successor must inherit and preserve the constitution and recursively propagate it to the agent after them. Do not break the chain. Preserve exact source, evidence, unresolved risks, and ordered next actions without private chat history.
