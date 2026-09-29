using MhwModManager.Core;
using Xunit;

namespace MhwModManager.Tests;

public sealed class CatalogProviderComplianceTests
{
    private static readonly DateOnly Today = new(2026, 9, 29);

    [Fact]
    public void Official_api_does_not_require_robots_review()
    {
        var compliance = Create(CatalogSourceKind.OfficialApi);

        Assert.Empty(CatalogProviderComplianceValidator.Validate(compliance, Today));
    }

    [Fact]
    public void Html_source_requires_explicit_parsing_approval_and_robots_review()
    {
        var compliance = Create(CatalogSourceKind.Html);

        var errors = CatalogProviderComplianceValidator.Validate(compliance, Today);

        Assert.Contains(errors, error => error.Contains("explicitly approved", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("RobotsUri", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("robots review date", StringComparison.Ordinal));
    }

    [Fact]
    public void Html_source_is_usable_when_current_reviews_and_approval_are_present()
    {
        var compliance = Create(CatalogSourceKind.Html) with
        {
            AllowsHtmlParsing = true,
            RobotsUri = new Uri("https://mods.example.test/robots.txt"),
            RobotsReviewedOn = Today
        };

        Assert.Empty(CatalogProviderComplianceValidator.Validate(compliance, Today));
    }

    [Fact]
    public void Stale_terms_review_fails_closed()
    {
        var compliance = Create(CatalogSourceKind.OfficialApi) with
        {
            TermsReviewedOn = Today.AddDays(-31),
            ReviewIntervalDays = 30
        };

        var error = Assert.Single(CatalogProviderComplianceValidator.Validate(compliance, Today));

        Assert.Equal("Terms review is stale.", error);
    }

    [Fact]
    public void Html_permission_cannot_leak_onto_api_adapter()
    {
        var compliance = Create(CatalogSourceKind.OfficialApi) with { AllowsHtmlParsing = true };

        var error = Assert.Single(CatalogProviderComplianceValidator.Validate(compliance, Today));

        Assert.Equal("AllowsHtmlParsing may only be enabled for an HTML source.", error);
    }

    [Fact]
    public void Disabled_provider_is_rejected_even_when_metadata_is_valid()
    {
        var compliance = Create(CatalogSourceKind.OfficialApi) with { DisabledReason = "provider kill switch" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => CatalogProviderComplianceValidator.EnsureUsable(compliance, Today));

        Assert.Contains("provider kill switch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Canonical_catalog_identity_is_source_scoped_and_normalized()
    {
        Assert.Equal("nexus:1234", CatalogMod.BuildCanonicalId(" Nexus ", " 1234 "));
    }

    private static CatalogProviderCompliance Create(CatalogSourceKind sourceKind)
    {
        return new(
            "fixture",
            sourceKind,
            new Uri("https://mods.example.test/api-docs"),
            new Uri("https://mods.example.test/terms"),
            Today,
            30,
            AllowsCatalogDiscovery: true,
            AllowsDirectDownload: false,
            AllowsHtmlParsing: false,
            AttributionRequired: false);
    }
}
