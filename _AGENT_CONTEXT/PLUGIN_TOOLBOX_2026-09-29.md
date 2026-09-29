# Plugin Toolbox implementation — 2026-09-29

This tranche materializes more of the plugin roadmap as reusable packages rather than leaving capabilities implicit inside Heaven Control Plane.

## Added packages

### heaven-workflows 0.1.0
- repo snapshot composition over structured Git capabilities;
- fixed project verification composition;
- bounded parallel scheduler for allowlisted structured capabilities;
- raw execution deliberately excluded from parallel scheduling.

### heaven-task-queue 0.1.0
- SQLite-backed dependency-aware task queue;
- task leases, bounded retries, worker heartbeat registration;
- atomic claim path using `BEGIN IMMEDIATE`;
- named resource locks for repo/worktree/service exclusivity;
- bounded persisted JSON payloads/results;
- explicit rule: secrets do not belong in queue payloads/results.

### heaven-local-ai 0.1.0
- local Ollama model listing/generation/summarization through Heaven Control Plane execution;
- model-name validation;
- prompt base64 encoding before PowerShell to prevent prompt text from becoming shell syntax;
- bounded prompt size and timeout.

## Verification

`plugins/verify.py` is the unified plugin gate. The Heaven self-hosted `plugin-toolbox-gate.yml` workflow compiles the plugin workspace, runs the existing control-plane gate plus every new package test suite, and checks patch whitespace.

This is a toolbox tranche, not the end of the roadmap. Remaining independent boundaries include broader process/service wrappers, browser automation, richer desktop actions through stable control-plane contracts, visual preprocessing, artifact/checkpoint durability, Docker/WSL/VM controls, package/database/network plugins, and multi-machine orchestration.
