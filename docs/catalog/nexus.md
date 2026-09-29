# Nexus Mods Provider

## Policy

Nexus integration is API-first. The application must not scrape nexusmods.com, bypass account tiers, ads, download pages, CAPTCHA/anti-bot controls, access controls, or rate limits.

Nexus' published API acceptable-use policy permits personal API keys for testing/personal use but requires a public-facing application to register with Nexus. The released public flow therefore needs the registered-application authentication path before this feature is broadly shipped. Development builds may use a personal key.

Credentials entered in the catalog UI are stored with Windows Credential Manager. `NEXUS_API_KEY` is accepted as a development override. A legacy key file is read only for compatibility; new secrets are not written to plaintext state or logs.

## Requests

`NexusCatalogProvider` uses the official API base and identifies requests with the application name/version. Remaining quota headers are normalized into `CatalogRateLimit` and exposed through provider health.

Current provider operations include:
- credential validation;
- games;
- trending/latest discovery;
- mod details;
- file lists and categories;
- permitted download-link resolution;
- assisted source-page fallback where direct resolution is not allowed.

The stable discovery surface does not currently provide full-text search across the complete Nexus corpus in this implementation. Search filters the normalized discovery cache; the UI must not imply complete site-wide results until a supported API path supplies them.

## File semantics

Nexus file categories are preserved as Main, Optional, Update, Miscellaneous, Old/Archived, Removed, or Unknown. The file picker defaults to one sensible Main file when available but keeps other choices visible. Optional/update files may be selected in addition; Main choices are mutually exclusive in the first milestone.

## Download restrictions

A failed/forbidden direct-download resolution does not trigger a workaround. The manager opens the legitimate Nexus file workflow and can continue after the user selects the legitimately downloaded archive. Both direct and assisted acquisitions feed the same local validation/import path.
