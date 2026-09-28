# Trainer Provenance — MHW Evidence Map

The trainer's rules are generic. This file preserves lightweight provenance showing which durable MHW incidents/audits motivated major doctrine. It is not a substitute for the project documents themselves.

| Generic lesson | MHW evidence |
| --- | --- |
| Canonical repository truth outranks stale prompts/chat | `AGENTS.md`, `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, support-branch integration history |
| Verification attaches to exact changed source | `_AGENT_CONTEXT/VERIFICATION.md`; LR-001 |
| Search/index results do not prove caller closure | LR-002 |
| Native/system failure may leave partial mutation | LR-003; Windows filesystem safety/test-gap audits |
| Lexical path containment is not physical containment | LR-004; Windows filesystem safety and recursive-source audits |
| Restartable migrations require ownership and retry convergence | LR-005; legacy migration recovery audit |
| Immutable/content-addressed publication requires independent byte ownership or enforced alias immutability | LR-011; `_AGENT_CONTEXT/LEGACY_MIGRATION_CAS_HARDLINK_RUNTIME_AUDIT.md` real-Windows reproduction |
| Shareable diagnostics need export-boundary sanitization | LR-006; diagnostics privacy audit |
| Entity deletion must retire live semantic references | LR-007; mod lifecycle referential-integrity audit |
| In-progress imports must stay invisible until commit-on-success | LR-008; import publication/catalog visibility audit |
| Cancellation/resource budgets must be enforced inside the actual streaming I/O boundary | `_AGENT_CONTEXT/ARCHIVE_EXTRACTION_RESOURCE_CANCELLATION_AUDIT.md` runtime reproduction |
| Automated diagnosis must validate controls before durable blame | LR-009; crash-bisector evidence audit |
| Integration state must record reviewed/merged/rejected/superseded work | `_AGENT_CONTEXT/SUPPORT_BRANCH_INTEGRATION_2026-09-27.md` |
| CI must verify the artifact users receive, not merely an intermediate build | `_AGENT_CONTEXT/VERIFICATION_INFRASTRUCTURE_AUDIT.md` and release-gate evidence |
| Continuity must survive agent loss and missing chat | `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md`, handoff validator, negative continuity fixtures |

When new projects provide equivalent or stronger evidence, extend or refine this map without putting transient project state into the generic rule bodies.
