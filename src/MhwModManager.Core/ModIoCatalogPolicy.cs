namespace MhwModManager.Core;

public static class ModIoCatalogPolicy
{
    public const string ProviderId = "modio";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: ProviderId,
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://docs.mod.io/restapi/"),
                TermsUri: new Uri("https://mod.io/terms"),
                TermsReviewedOn: new DateOnly(2026, 9, 29),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: true,
                AllowsHtmlParsing: false,
                AttributionRequired: false);
        }
    }
}
