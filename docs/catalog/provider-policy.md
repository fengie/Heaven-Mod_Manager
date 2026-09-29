# Provider Integration Policy

Research baseline: 2026-09-29.

## Source precedence

1. official public API;
2. official application integration;
3. RSS/Atom or official structured feed;
4. public structured metadata explicitly intended for third-party consumption;
5. permitted HTML parsing after Terms + robots + API review;
6. browser-assisted/manual acquisition.

Do not bypass authentication, premium/account restrictions, ads or required download pages, CAPTCHA, Cloudflare/anti-bot controls, rate limits, signed-download protection, or other access controls. Do not rehost third-party archives.

## Mandatory provider compliance record

Every provider must record:
- provider/source ID and source kind;
- official docs/API URL;
- terms URL and review date;
- robots URL/review date when crawling applies;
- allowed/forbidden operations;
- authentication and secret boundary;
- attribution requirements;
- rate/concurrency/backoff policy;
- cache TTL and conditional-request support;
- direct-download rules;
- compatibility verification date;
- kill-switch/disabled state.

Crawler-enabled providers fail closed when required compliance data is missing or stale.

## Network behavior

- HTTPS-only direct archive acquisition.
- Honest stable application User-Agent.
- Prefer conditional HTTP requests.
- Respect 429, Retry-After, quotas, and provider-specific concurrency limits.
- Bounded exponential backoff; no retry storms.
- Deduplicate identical concurrent requests.
- Cache metadata to avoid unnecessary provider load.
- Isolate provider failures.

## Scraping

HTML parsing is a last-resort adapter class.

A scraper must:
- target only explicitly approved paths/selectors;
- obey robots and terms;
- have deterministic parser fixtures;
- detect layout/schema drift and disable itself rather than silently corrupt data;
- never use browser impersonation or anti-bot evasion;
- never harvest private/user data;
- never use authenticated cookies/session tokens without an explicitly supported provider contract;
- never turn third-party pages into a mirrored content corpus.

When an API/feed exists for the needed data, use it instead.

## Downloads and secrets

Every archive is untrusted. Sanitize filenames, bound download sizes, isolate temporary files, calculate hashes, and pass content through existing archive/import/deployment safety boundaries.

Signed/expiring URLs are ephemeral acquisition data, not durable source identity.

Never place provider secrets, cookies, bearer tokens, API keys, or signed URLs in logs, catalog rows, cache files, diagnostics, crash bundles, or source control.

When policy/capability is uncertain, disable unsupported automation and retain a compliant assisted source workflow.
