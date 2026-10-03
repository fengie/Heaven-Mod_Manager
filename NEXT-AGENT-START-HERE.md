# v8.8.89 privacy, legal & accessibility baseline — handoff

Canonical repository: `fengie/mhw-mods`  
Global bootstrap/training: `fengie/heaven-toolbox@main`  
Canonical target branch: `main`  
Change set: issue #710

## v8.8.89 behavior

- Bundle and expose Privacy, Terms, Refund, Cookie, Data Deletion, Third-Party Notices, and Project/Support documents.
- Keep web-only controls truthful: the current native app has no first-party account, ads, browser cookies, in-app payment flow, consumer-review system, or marketing email.
- Treat future accounts/hosted personal data, analytics/ads, embedded web tracking, payments, reviews/testimonials, marketing email, child-directed data collection, telemetry uploads, new packages, and new bundled fonts/artwork as compliance-review triggers.
- Provide a Settings action to open the manager-owned state folder and deletion/reset guidance that does not erase durable Mods or archives by implication.
- Regression-check central package/version notice coverage.
- Complete UI Automation naming for MainWindow preview/artwork images, keep explicit keyboard tab navigation/focus visibility, and test the core palette against WCAG AA normal-text contrast thresholds.
- Preserve all existing updater, filesystem, release, and safety boundaries.

## Verification boundary

Last closed canonical Windows source remains v8.8.88 `d0816f3c495ff055df517ce0b021e41dd19596f6` / run `37143560604`.

v8.8.89 changes shipped app source/XAML/tests/content and release metadata, so earlier greens do not authorize release closure. Require fresh exact-input canonical Windows verification for the integrated v8.8.89 source before calling the patch closed/released.

## Execution/offload note

The implementing ChatGPT session exposed authenticated GitHub mutation but no callable Heaven Local Bridge / Agent Control execution surface. Implementation therefore used the GitHub path and repository CI; no local Heaven/Windows test is claimed by that session.

## Legal/compliance scope

`docs/COMPLIANCE-BASELINE.md` is an engineering baseline, not a guarantee against lawsuits or jurisdiction-specific legal advice. Do not invent a company/legal entity, physical address, paid-service terms, cookie consent, age gates, or unsubscribe flows unless the product actually introduces the underlying facts/features and they are reviewed.

## Next action

After integration, obtain fresh canonical Windows verification for the exact v8.8.89 source and persist truthful evidence. Then resume the highest-priority non-overlapping item from `_AGENT_CONTEXT/PROJECT_PLAN.md`.

## Successor obligation

Bootstrap from current `fengie/heaven-toolbox@main`, then current MHW `main`; read `_AGENT_CONTEXT/CONTINUITY_PROTOCOL.md` and task-relevant `_AGENT_CONTEXT/LEARNED_RULES.md`. Preserve exact-input verification, compliance assumptions/change triggers, updater public/private parity/provenance, exact SDK policy, filesystem containment, and durable-evidence privacy. Propagate this obligation recursively.

**Do not break the chain.**
