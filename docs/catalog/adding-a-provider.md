# Adding a Catalog Provider

1. Research the source before coding: official API/integration, feeds/structured metadata, authentication, limits, download rules, redistribution restrictions, Terms, and robots directives when HTML parsing is even being considered.
2. Record the decision in `providers.md`. API/integration/feed options take precedence over parsing. Never implement bypasses for authentication, CAPTCHA, paywalls, anti-bot controls, signed links, or rate limits.
3. Implement `IModCatalogProvider` behind the normalized model. Provider-only data belongs in `ProviderMetadata`.
4. Declare only capabilities that actually work.
5. Keep credentials behind the credential boundary; never log or persist plaintext tokens.
6. Add provider fixtures and deterministic tests for normalization, malformed responses, auth/rate-limit failures, file variants, and outage isolation.
7. Register the adapter in `ModCatalogProviderRegistry`. Do not add a provider-specific catalog UI.
8. Verify stale-cache behavior and assisted-download behavior if direct download is unavailable.
9. Document any unsupported capability instead of guessing.

A provider is not complete merely because metadata can be fetched. File identity, exact user choice, compliant acquisition, failure isolation, and installed-origin persistence must remain correct.
