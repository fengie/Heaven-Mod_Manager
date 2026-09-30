using System.Globalization;

namespace MhwModManager.Core;

internal static class ThunderstoreCatalogIdentity
{
    private const int MaxCommunityIdentifierLength = 80;

    public static string NormalizeCommunityIdentifier(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > MaxCommunityIdentifierLength
            || normalized[0] == '-'
            || normalized[^1] == '-')
        {
            throw new ArgumentException(
                "Thunderstore community identifiers must be 1..80 lowercase letters, digits, or hyphens and cannot start or end with a hyphen.",
                parameterName);
        }

        foreach (var character in normalized)
        {
            if ((character is >= 'a' and <= 'z')
                || (character is >= '0' and <= '9')
                || character == '-')
            {
                continue;
            }

            throw new ArgumentException(
                "Thunderstore community identifiers must contain only letters, digits, and hyphens.",
                parameterName);
        }

        return normalized;
    }

    public static string NormalizeUuid(string value, string parameterName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (!Guid.TryParse(value.Trim(), out var parsed) || parsed == Guid.Empty)
            throw new ArgumentException("Thunderstore identifiers must be non-empty UUIDs.", parameterName);

        return parsed.ToString("D", CultureInfo.InvariantCulture);
    }

    public static string BuildProviderModId(string communityIdentifier, string packageUuid)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var community = NormalizeCommunityIdentifier(communityIdentifier, nameof(communityIdentifier));
        var packageId = NormalizeUuid(packageUuid, nameof(packageUuid));
        return $"{community}:{packageId}";
    }

    public static bool TryParseProviderModId(
        string providerModId,
        out string communityIdentifier,
        out string packageUuid)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        communityIdentifier = string.Empty;
        packageUuid = string.Empty;

        if (string.IsNullOrWhiteSpace(providerModId))
            return false;

        var split = providerModId.IndexOf(':', StringComparison.Ordinal);
        if (split <= 0
            || split == providerModId.Length - 1
            || providerModId.IndexOf(':', split + 1) >= 0)
        {
            return false;
        }

        try
        {
            communityIdentifier = NormalizeCommunityIdentifier(
                providerModId[..split],
                nameof(providerModId));
            packageUuid = NormalizeUuid(providerModId[(split + 1)..], nameof(providerModId));
            return true;
        }
        catch (ArgumentException)
        {
            communityIdentifier = string.Empty;
            packageUuid = string.Empty;
            return false;
        }
    }

    public static bool IsTrustedHost(string host)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return host.Equals("thunderstore.io", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".thunderstore.io", StringComparison.OrdinalIgnoreCase);
    }
}
