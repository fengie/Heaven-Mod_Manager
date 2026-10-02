# v8.8.73 release provenance hardening — canonical-ready state

v8.8.72 PR #581 is the verified predecessor boundary. This change set implements issue #583 without changing mod-manager runtime behavior.

## Behavior

- Release artifact provenance uses the exact updater ZIP name and SHA-256 declared by the verified build manifest, with an independent pre-attestation hash recomputation.
- Supported GitHub repositories create SLSA build provenance and verify it before public/private updater publication.
- Private repositories do not falsely claim provenance when the current account lacks GitHub Enterprise Cloud; attestation is enabled only with the explicit supported-tier variable.
- Publication remains freshness-gated, public-first, immutable, parity-checked, and non-cancellable once the transaction reaches mutation.

## Verification boundary

The last closed predecessor is v8.8.72 exact head `c9904b0ba85854ec332bd14778e5e5531a50d582`. Fresh Workflow Feature, Updater Publication, MHW Product Security, and Heaven Toolbox Ownership gates are required on the exact final v8.8.73 head.

Every successor must read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, retrieve task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`, and recursively propagate the continuity obligation.
