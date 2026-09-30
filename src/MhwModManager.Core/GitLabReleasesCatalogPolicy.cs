namespace MhwModManager.Core;

public static class GitLabReleasesCatalogPolicy
{
    public const string ProviderId = "gitlab-releases";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: ProviderId,
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://docs.gitlab.com/api/releases/"),
                TermsUri: new Uri("https://about.gitlab.com/terms/"),
                TermsReviewedOn: new DateOnly(2026, 9, 30),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: true,
                AllowsHtmlParsing: false,
                AttributionRequired: false);
        }
    }
}
