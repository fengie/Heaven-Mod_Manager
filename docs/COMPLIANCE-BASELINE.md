# Product Compliance Baseline

**Baseline date:** October 3, 2026
**Scope:** current native Windows desktop application.
**Purpose:** turn the common "20 things" app-compliance checklist into concrete product controls without pretending web/SaaS obligations apply when the underlying feature does not exist.

This is an engineering baseline, not a substitute for jurisdiction-specific legal review.

| # | Checklist item | Current product control |
| --- | --- | --- |
| 1 | Privacy policy | Root `PRIVACY.md`, bundled into the app output and linked from Settings. |
| 2 | Terms of service/use | Root `TERMS.md`, bundled and linked from Settings. |
| 3 | Refund policy | `REFUND_POLICY.md` states the current app takes no payment and explains third-party transactions. |
| 4 | Cookie policy | `COOKIE_POLICY.md` documents that the native app does not use browser cookies. |
| 5 | Cookie consent banner | Not shown because the current native app has no non-essential cookies. Adding cookie-setting embedded web content requires consent/policy work before release. |
| 6 | Form consents | No first-party account/marketing form exists. Optional network/background features are explicit user settings; future personal-data forms require purpose-specific consent. |
| 7 | No unnecessary data | Local-first storage, bounded diagnostics/support behavior, and documented data categories/minimization. |
| 8 | Audit third-party SDKs | `THIRD_PARTY_NOTICES.md` is version-checked against `Directory.Packages.props`. |
| 9 | Remove dark patterns | Optional features remain reversible settings; destructive/staged operations have explicit confirmation/recovery semantics. |
| 10 | Remove hidden fees | Current app has no first-party payments, subscriptions, or paid upgrade flow. |
| 11 | Remove fake reviews | Current app has no consumer-review system; `TERMS.md` prohibits fabricated reviews/endorsements. |
| 12 | Remove unsupported claims | Terms disclaim unsupported guarantees; release evidence must remain tied to exact source/artifacts under repository rules. |
| 13 | Accessibility alt text | WPF preview/artwork images expose UI Automation names; regression tests reject unnamed image controls. |
| 14 | Fix color contrast | Core palette text/background combinations are regression-tested against WCAG 2.2 AA text thresholds. |
| 15 | Keyboard navigation | WPF shell explicitly keeps tab navigation enabled and visible keyboard focus styling. |
| 16 | Add business details | `SUPPORT.md` identifies the project/publisher and distribution channel without inventing a legal entity/address. Formal commercial-entity details are a release prerequisite if that status changes. |
| 17 | Age consent for kids' data | Current app is general audience and has no first-party online personal-data collection from children. If that changes, child-data/age-consent review becomes a release gate. |
| 18 | Unsubscribe link in emails | Not applicable: current app sends no marketing email. A future marketing-email feature requires unsubscribe handling before release. |
| 19 | License fonts/images | Third-party package licenses are inventoried; system fonts are not redistributed; asset provenance is required for bundled artwork/icons. |
| 20 | Data deletion request | `DATA-DELETION.md` documents local self-service deletion/reset and distinguishes manager state from durable Mods/archive content. |

## Change-trigger rules

The following feature additions automatically require a compliance review before release: user accounts, hosted personal data, analytics/advertising SDKs, embedded web views, browser cookies, payments, reviews/testimonials, marketing email, child-directed functionality, new telemetry uploads, new third-party packages, or new bundled fonts/artwork.

The integration test `ComplianceSurfaceTests` protects the current assumptions mechanically.
