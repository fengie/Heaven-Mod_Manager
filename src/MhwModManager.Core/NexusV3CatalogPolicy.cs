namespace MhwModManager.Core;

public static class NexusV3CatalogPolicy
{
    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: "nexus",
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://github.com/Nexus-Mods/Vortex/blob/master/packages/nexus-api-v3/schema/openapi.yaml"),
                TermsUri: new Uri("https://help.nexusmods.com/article/114-api-acceptable-use-policy"),
                TermsReviewedOn: new DateOnly(2026, 9, 29),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: false,
                AllowsHtmlParsing: false,
                AttributionRequired: false);
        }
    }
}
