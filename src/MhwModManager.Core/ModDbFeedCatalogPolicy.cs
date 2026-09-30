namespace MhwModManager.Core;

public static class ModDbFeedCatalogPolicy
{
    public const string ProviderId = "moddb-rss";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: ProviderId,
                SourceKind: CatalogSourceKind.Feed,
                DocumentationUri: new Uri("https://www.moddb.com/rss"),
                TermsUri: new Uri("https://www.moddb.com/groups/moddb/terms"),
                TermsReviewedOn: new DateOnly(2026, 9, 30),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: false,
                AllowsHtmlParsing: false,
                AttributionRequired: true,
                AttributionText: "Mod DB - Game Development");
        }
    }
}
