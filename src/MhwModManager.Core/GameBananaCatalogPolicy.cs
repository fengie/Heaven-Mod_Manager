namespace MhwModManager.Core;

public static class GameBananaCatalogPolicy
{
    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: "gamebanana",
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://api.gamebanana.com/"),
                TermsUri: new Uri("https://gamebanana.com/wikis/334"),
                TermsReviewedOn: new DateOnly(2026, 9, 29),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: false,
                AllowsHtmlParsing: false,
                AttributionRequired: false);
        }
    }
}
