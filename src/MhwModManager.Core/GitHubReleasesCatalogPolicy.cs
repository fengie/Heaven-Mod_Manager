namespace MhwModManager.Core;

public static class GitHubReleasesCatalogPolicy
{
    public const string ProviderId = "github-releases";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: ProviderId,
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://docs.github.com/en/rest/releases/releases"),
                TermsUri: new Uri("https://docs.github.com/en/site-policy/github-terms/github-terms-of-service"),
                TermsReviewedOn: new DateOnly(2026, 9, 29),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: true,
                AllowsHtmlParsing: false,
                AttributionRequired: false);
        }
    }
}
