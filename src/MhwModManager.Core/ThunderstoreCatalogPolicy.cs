namespace MhwModManager.Core;

public static class ThunderstoreCatalogPolicy
{
    public const string ProviderId = "thunderstore";

    public const string PendingTermsReason =
        "Thunderstore's public API and Privacy Policy were reviewed on 2026-09-29, but a distinct current service/API Terms of Service URL was not discoverable. Keep automated catalog access disabled until that legal reference is confirmed.";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId: ProviderId,
                SourceKind: CatalogSourceKind.OfficialApi,
                DocumentationUri: new Uri("https://thunderstore.io/api/docs/"),
                TermsUri: new Uri("https://pages.thunderstore.io/p/privacy-policy"),
                TermsReviewedOn: new DateOnly(2026, 9, 29),
                ReviewIntervalDays: 30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: true,
                AllowsHtmlParsing: false,
                AttributionRequired: false,
                DisabledReason: PendingTermsReason);
        }
    }
}
