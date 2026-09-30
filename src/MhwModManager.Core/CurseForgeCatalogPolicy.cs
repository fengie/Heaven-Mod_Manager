namespace MhwModManager.Core;

public static class CurseForgeCatalogPolicy
{
    public const string ProviderId = "curseforge";
    public const string DisabledReason =
        "CurseForge 3rd Party API Terms reviewed on 2026-09-30 prohibit saving or caching API data. The federated catalog requires source-aware persistence and stale-while-revalidate caching, so automated CurseForge catalog access stays disabled until an approved non-caching design or revised terms are confirmed.";

    public static CatalogProviderCompliance Compliance
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return new CatalogProviderCompliance(
                ProviderId,
                CatalogSourceKind.OfficialApi,
                new Uri("https://support.curseforge.com/support/solutions/articles/9000208346-about-the-curseforge-api-and-how-to-apply-for-a-key"),
                new Uri("https://support.curseforge.com/support/solutions/articles/9000207405-curseforge-3rd-party-api-terms-and-conditions"),
                new DateOnly(2026, 9, 30),
                30,
                AllowsCatalogDiscovery: true,
                AllowsDirectDownload: false,
                AllowsHtmlParsing: false,
                AttributionRequired: false,
                DisabledReason: DisabledReason);
        }
    }
}
