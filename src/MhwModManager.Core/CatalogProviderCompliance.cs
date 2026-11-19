using System.Net.Http;

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
    public bool Enabled
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return string.IsNullOrWhiteSpace(DisabledReason);
        }
    }
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

/// <summary>
/// Produces actionable, non-sensitive UI diagnostics without bypassing provider use policy.
/// The underlying provider exception is retained only in the master debug log.
/// </summary>
public static class CatalogProviderFailureExplanation
{
    public static string Describe(CatalogProviderCompliance compliance, Exception exception, DateOnly today)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(compliance);
        ArgumentNullException.ThrowIfNull(exception);

        if (!compliance.Enabled)
            return "This provider is disabled by its configured use policy. A maintainer must review access.";

        var validationErrors = CatalogProviderComplianceValidator.Validate(compliance, today);
        if (validationErrors.Contains("Terms review is stale.", StringComparer.Ordinal))
        {
            var expires = compliance.TermsReviewedOn.AddDays(compliance.ReviewIntervalDays);
            return $"Provider terms review expired {expires:yyyy-MM-dd}. A verified maintainer review is required; retrying cannot restore access. Browse the provider website meanwhile.";
        }

        if (validationErrors.Count > 0)
            return "Provider policy or configuration validation failed. See diagnostics and obtain a verified review before retrying.";

        return exception switch
        {
            TaskCanceledException => "Provider request timed out. Retry later or browse the provider website.",
            HttpRequestException => "Provider network request failed. Check connectivity or browse the provider website.",
            InvalidDataException => "Provider response could not be read safely. The adapter may need updating.",
            _ => "Provider request failed. See the master debug log for details or browse the provider website."
        };
    }
}
