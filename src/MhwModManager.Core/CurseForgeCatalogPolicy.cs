namespace MhwModManager.Core;

public static class CurseForgeCatalogPolicy
{
    public const string ProviderId = "curseforge";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: ProviderId,
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://docs.curseforge.com/rest-api/"),
                TermsUri: new Uri("https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key"),
                TermsReviewedOn: new DateOnly(2026, 9, 30),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: true,
                AllowsHtmlParsing: false,
                AttributionRequired: true,
                AttributionText: "CurseForge");
        }
    }
}
