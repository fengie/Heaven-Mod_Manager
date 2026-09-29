# Catalog Dependency Resolution

Dependencies are represented by `CatalogDependency` so providers can expose requirements without provider-specific UI.

The first Nexus milestone defines the model but does not yet automate dependency graphs because the implemented Nexus adapter does not currently return reliable normalized dependency data. Until a provider supplies it, the catalog must not invent requirements.

Future implementation requirements:
- distinguish required, recommended, DLC/framework, optional-patch, and cross-provider requirements when the source exposes enough evidence;
- build a graph by stable provider/canonical identity;
- detect cycles before acquisition;
- resolve installed requirements using catalog-origin metadata;
- offer automatic installation only when every required node has an unambiguous legitimate acquisition path;
- surface unresolved/external requirements in normal language and fail closed rather than guessing.
