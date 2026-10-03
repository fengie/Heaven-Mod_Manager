# v8.8.89 privacy, legal & accessibility baseline — canonical state

v8.8.89 turns the user's 20-point compliance checklist into native-desktop product controls without adding misleading web-only consent UI for features the application does not have.

## Behavior

- Privacy, terms-of-use, refund, cookie, local data-deletion, support/project-detail, and third-party notices are bundled into the application output and reachable from Settings.
- The current native build explicitly documents that it has no first-party account system, advertising, browser cookies, in-app payments, consumer-review system, or marketing email. Introducing those features is a compliance-review trigger rather than permission to inherit today's assumptions.
- Settings can open the active manager state directory and the deletion guide explains how to reset local manager data without silently deleting durable Mods or archives.
- Third-party notices enumerate the centrally managed package inventory; deterministic regression coverage makes package/version drift visible.
- All current MainWindow preview/artwork Image controls expose UI Automation names.
- The WPF shell keeps explicit tab navigation and visible keyboard focus behavior.
- Deterministic tests check the core text/background palette against WCAG AA 4.5:1 normal-text contrast thresholds.
- `docs/COMPLIANCE-BASELINE.md` maps all 20 checklist items to implemented controls or explicit not-applicable/change-trigger rules.

## Verification boundary

Current hosted-Windows closure: v8.8.89 source `93ef8af8bbaaf549c1a4bb4db1ba1c2fef71e0b1` passed run `37149187890` with 0 failed checks. Exact evidence: `_AGENT_CONTEXT/EVIDENCE/v8.8.89-heaven-windows-closure.log`.

The tested source remains `93ef8af8bbaaf549c1a4bb4db1ba1c2fef71e0b1` even though persistence creates a later evidence-only commit. Any source, workflow, test, or release-input change after that SHA requires fresh exact-input verification; an evidence-only commit must never be treated as the tested source.

## Legal/compliance scope

This repository baseline is an engineering control set, not a promise that the application cannot be sued and not a substitute for jurisdiction-specific legal advice. Formal company/entity/address disclosures are intentionally not invented. If the product becomes a paid/commercial service or adds hosted personal data, child-directed collection, analytics/ads, embedded cookie-setting web content, marketing email, reviews/testimonials, or new telemetry uploads, perform a new compliance review before release.

## Remaining independent work

Keep #669 storage lifecycle, #559/#558 catalog UX/scale, #350/#354 external trust/admin prerequisites, #709 reproducible NuGet restore, and remaining recovery/installed-Windows acceptance independent.

Every successor must preserve the permanent continuity constitution, exact-input verification, updater publication/parity/provenance invariants, exact SDK policy, durable-evidence privacy, and recursively propagate the same obligation.
