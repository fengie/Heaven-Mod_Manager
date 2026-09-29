# Provider Integration Policy

Use this precedence for every source:

1. official public API;
2. official application integration;
3. RSS/Atom or official structured feed;
4. public structured metadata;
5. permitted HTML parsing only after a documented Terms/robots/API review;
6. browser-assisted/manual-download fallback.

Do not bypass authentication, premium/account restrictions, ads or mandatory download pages, CAPTCHA, Cloudflare/anti-bot controls, rate limits, signed-download protection, or other access controls. Do not rehost third-party archives as a centralized mirror.

Provider failures are isolated: one provider outage must not make cached data from another provider unusable. Respect provider-specific TTL/rate policies and use conditional/incremental requests where supported.

Every downloaded archive is untrusted. Sanitize filenames, enforce bounded download sizes, keep temporary files isolated, calculate hashes, and pass content through the existing safe archive/import/deployment boundaries. Never put provider secrets in logs, database rows, cache files, diagnostics, or source control.

When policy or capability is uncertain, disable the unsupported automation and retain a compliant assisted source workflow rather than attempting a workaround.
