# Normalized Catalog Schema

Core normalized types live in `MhwModManager.Core/CatalogDomain.cs`.

- `CatalogGame`: provider-neutral game identity/display metadata.
- `CatalogMod`: canonical/provider identity, game, title/summary/description, author/version, category/tags, images, dates, popularity metadata, dependencies, source URL, files, and opaque provider metadata.
- `CatalogModFile`: provider/mod/file identity, name/filename, category, version, size, description, upload time, required/recommended flags, dependencies, and opaque provider metadata.
- `CatalogDependency`: named requirement with optional provider identity/source URL.
- `CatalogImage`: image URL plus caption/thumbnail role.
- `CatalogProviderHealth`: connection state, diagnostic message, and normalized rate-limit snapshot.
- `InstalledCatalogOrigin`: local mod ID plus exact provider/mod/file/version/source/hash provenance.

`ProviderMetadata` is the escape hatch for provider-specific fields; those fields must not leak into generic UI/service contracts.

## Identity

`CatalogMod.BuildCanonicalId` currently provides stable exact provider identity. Cross-provider deduplication must require stronger signals such as explicit cross-links, repositories, package metadata, or content hashes. Do not fuzzy-merge unrelated projects solely because names/authors look similar.
