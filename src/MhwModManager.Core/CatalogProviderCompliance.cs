namespace MhwModManager.Core;

public enum CatalogSourceKind
{
    OfficialApi,
    OfficialIntegration,
    Feed,
    StructuredMetadata,
    Html,
    Interop
}

public sealed record CatalogProviderCompliance(
    string ProviderId,
    CatalogSourceKind SourceKind,
    Uri DocumentationUri,
    Uri TermsUri,
    DateOnly TermsReviewedOn,
    int ReviewIntervalDays,
    bool AllowsCatalogDiscovery,
    bool AllowsDirectDownload,
    bool AllowsHtmlParsing,
    bool AttributionRequired,
    Uri? RobotsUri = null,
    DateOnly? RobotsReviewedOn = null,
    string? AttributionText = null,
    string? DisabledReason = null)
{
    public bool Enabled => string.IsNullOrWhiteSpace(DisabledReason);
}

public static class CatalogProviderComplianceValidator
{
    public static IReadOnlyList<string> Validate(CatalogProviderCompliance compliance, DateOnly today)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(compliance);

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(compliance.ProviderId))
            errors.Add("ProviderId is required.");

        if (!compliance.DocumentationUri.IsAbsoluteUri)
            errors.Add("DocumentationUri must be absolute.");

        if (!compliance.TermsUri.IsAbsoluteUri)
            errors.Add("TermsUri must be absolute.");

        if (compliance.ReviewIntervalDays <= 0)
            errors.Add("ReviewIntervalDays must be positive.");
        else if (compliance.TermsReviewedOn.AddDays(compliance.ReviewIntervalDays) < today)
            errors.Add("Terms review is stale.");

        if (compliance.SourceKind == CatalogSourceKind.Html)
        {
            if (!compliance.AllowsHtmlParsing)
                errors.Add("HTML source is not explicitly approved for parsing.");

            if (compliance.RobotsUri is null || !compliance.RobotsUri.IsAbsoluteUri)
                errors.Add("HTML source requires an absolute RobotsUri.");

            if (compliance.RobotsReviewedOn is null)
            {
                errors.Add("HTML source requires a robots review date.");
            }
            else if (compliance.ReviewIntervalDays > 0 &&
                     compliance.RobotsReviewedOn.Value.AddDays(compliance.ReviewIntervalDays) < today)
            {
                errors.Add("Robots review is stale.");
            }
        }
        else if (compliance.AllowsHtmlParsing)
        {
            errors.Add("AllowsHtmlParsing may only be enabled for an HTML source.");
        }

        if (compliance.AttributionRequired && string.IsNullOrWhiteSpace(compliance.AttributionText))
            errors.Add("AttributionText is required when attribution is mandatory.");

        return errors;
    }

    public static void EnsureUsable(CatalogProviderCompliance compliance, DateOnly today)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(compliance);
        var errors = Validate(compliance, today);

        if (!compliance.Enabled)
            throw new InvalidOperationException($"Catalog provider '{compliance.ProviderId}' is disabled: {compliance.DisabledReason}");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Catalog provider '{compliance.ProviderId}' failed compliance validation: {string.Join(" ", errors)}");
    }
}
