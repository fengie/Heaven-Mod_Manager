using MhwModManager.Core;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Synthetic compliance metadata for tests of catalog indexing, paging, transactions and
/// failure isolation. These tests are NOT terms-of-service reviews of a real provider.
/// Production providers still validate their independently reviewed records against UTC.
/// </summary>
internal static class CatalogComplianceFixture
{
    public static CatalogProviderCompliance Active(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        return new CatalogProviderCompliance(
            ProviderId: providerId,
            SourceKind: CatalogSourceKind.OfficialApi,
            DocumentationUri: new Uri("https://mods.example.test/api"),
            TermsUri: new Uri("https://mods.example.test/terms"),
            TermsReviewedOn: DateOnly.FromDateTime(DateTime.UtcNow),
            ReviewIntervalDays: 30,
            AllowsCatalogDiscovery: true,
            AllowsDirectDownload: false,
            AllowsHtmlParsing: false,
            AttributionRequired: false);
    }
}
