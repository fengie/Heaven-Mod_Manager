using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MhwModManager.Core;

/// <summary>
/// An observed claim in a third-party Crimson Desert Field-JSON manifest.
/// These are advisory metadata keys, not compiled patches or evidence that the
/// target table/asset exists in the installed version of the game.
/// </summary>
public sealed record CrimsonDesertFieldClaim(
    string Kind, string TargetKey, string ValueFingerprint, bool IsSimpleSet);

public sealed record CrimsonDesertFieldInspection(
    bool IsValid, string Reason, IReadOnlyList<CrimsonDesertFieldClaim> Claims);

/// <summary>
/// Bounded, read-only structural inspection of documented Field-JSON v3/v3.1.
/// It never imports a loader, opens game archives or authorizes live deployment.
/// </summary>
public static class CrimsonDesertFieldManifestInspector
{
    public const int MaxUtf8Bytes = 256 * 1024;
    public const int MaxTargets = 256;
    public const int MaxClaims = 4096;

    public static CrimsonDesertFieldInspection Inspect(string json)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxUtf8Bytes)
            return new(false, "Field-JSON is empty or exceeds the bounded 256 KiB inspection limit.", []);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("format", out var format) ||
                format.ValueKind != JsonValueKind.Number ||
                !format.TryGetInt32(out var major) || major != 3)
                return new(false, "Field-JSON requires a recognized format 3 manifest.", []);

            if (root.TryGetProperty("format_minor", out var minor) &&
                (minor.ValueKind != JsonValueKind.Number ||
                 !minor.TryGetInt32(out var minorValue) || minorValue is < 0 or > 1))
                return new(false, "Field-JSON minor version is unsupported.", []);

            var targets = new List<JsonElement>();
            if (root.TryGetProperty("targets", out var targetArray) &&
                targetArray.ValueKind == JsonValueKind.Array)
                targets.AddRange(targetArray.EnumerateArray());
            else if (root.TryGetProperty("target", out var singular) &&
                     singular.ValueKind == JsonValueKind.String)
                targets.Add(root);
            if (targets.Count is < 1 or > MaxTargets)
                return new(false, "Field-JSON must contain 1 to 256 targets.", []);

            var claims = new List<CrimsonDesertFieldClaim>();
            var uniqueKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets)
            {
                if (target.ValueKind != JsonValueKind.Object)
                    return new(false, "Field-JSON target must be an object.", []);
                string? kind = target.TryGetProperty("kind", out var kindElement)
                    && kindElement.ValueKind == JsonValueKind.String ? kindElement.GetString() : null;
                var tableName = target.TryGetProperty("table", out var table)
                    && table.ValueKind == JsonValueKind.String ? table.GetString() : null;
                if (tableName is null && target.TryGetProperty("file", out var fileTable)
                    && fileTable.ValueKind == JsonValueKind.String &&
                    (fileTable.GetString()?.EndsWith(".pabgb", StringComparison.OrdinalIgnoreCase) == true
                    || fileTable.GetString()?.EndsWith(".staticinfobody", StringComparison.OrdinalIgnoreCase) == true))
                    tableName = fileTable.GetString();
                if (tableName is null && target.TryGetProperty("target", out var legacyTarget)
                    && legacyTarget.ValueKind == JsonValueKind.String)
                    tableName = legacyTarget.GetString();
                kind ??= tableName is null ? "" : "table";

                if (kind.Equals("table", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(tableName) || tableName.Length > 256 ||
                        !PathRules.IsSafeArchiveRelativePath(tableName))
                        return new(false, "Field-JSON table target is missing or unsafe.", []);
                    var tableKey = Path.GetFileNameWithoutExtension(tableName.Replace('\\', '/'))
                        .ToLowerInvariant();
                    if (tableKey.Length == 0)
                        return new(false, "Field-JSON table identity is empty.", []);
                    if (!target.TryGetProperty("ops", out var operations) &&
                        !target.TryGetProperty("intents", out operations))
                        return new(false, "Field-JSON table target has no operations.", []);
                    if (operations.ValueKind != JsonValueKind.Array ||
                        operations.GetArrayLength() is < 1 or > MaxClaims)
                        return new(false, "Field-JSON table operation count is invalid.", []);

                    foreach (var operation in operations.EnumerateArray())
                    {
                        if (operation.ValueKind != JsonValueKind.Object)
                            return new(false, "Field-JSON operation is not an object.", []);
                        var opName = operation.TryGetProperty("op", out var opElement) &&
                            opElement.ValueKind == JsonValueKind.String ? opElement.GetString() : "set";
                        if (opName is null || !new[] { "set", "array_append", "clone_record", "new_record", "delete_record" }
                            .Contains(opName, StringComparer.OrdinalIgnoreCase))
                            return new(false, "Field-JSON contains an unrecognized operation.", []);
                        string? row = null;
                        if (operation.TryGetProperty("key", out var key) &&
                            key.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                            row = key.ToString();
                        else if (operation.TryGetProperty("entry", out var entry) &&
                            entry.ValueKind == JsonValueKind.String)
                            row = entry.GetString();
                        if (string.IsNullOrWhiteSpace(row) || row.Length > 256)
                            return new(false, "Field-JSON operation needs a bounded record key.", []);

                        var isSimpleSet = opName.Equals("set", StringComparison.OrdinalIgnoreCase);
                        var fieldName = operation.TryGetProperty("field", out var field) &&
                            field.ValueKind == JsonValueKind.String ? field.GetString() : null;
                        if (isSimpleSet && (string.IsNullOrWhiteSpace(fieldName) || fieldName.Length > 256))
                            return new(false, "Field-JSON set operation needs a bounded field name.", []);
                        if (!operation.TryGetProperty("value", out var newValue) &&
                            !operation.TryGetProperty("new", out newValue) && isSimpleSet)
                            return new(false, "Field-JSON set operation needs a value.", []);
                        // Non-set operations are recognized for intake, but never
                        // asserted to compose without the actual native writer.
                        var targetKey = "table:" + tableKey + ":" + row.ToLowerInvariant() +
                            ":" + (isSimpleSet ? fieldName!.ToLowerInvariant() : "*");
                        if (!uniqueKeys.Add(targetKey))
                            return new(false, "Field-JSON changes the same record/field more than once; review order.", []);
                        var valueJson = isSimpleSet ? newValue.GetRawText() : operation.GetRawText();
                        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(valueJson)));
                        claims.Add(new("table", targetKey, digest, isSimpleSet));
                        if (claims.Count > MaxClaims)
                            return new(false, "Field-JSON exceeds the bounded mutation count.", []);
                    }
                }
                else if (kind.Equals("asset", StringComparison.OrdinalIgnoreCase))
                {
                    if (!target.TryGetProperty("file", out var fileValue) ||
                        fileValue.ValueKind != JsonValueKind.String ||
                        !target.TryGetProperty("vpath", out var vpathValue) ||
                        vpathValue.ValueKind != JsonValueKind.String)
                        return new(false, "Field-JSON asset needs source file and virtual target path.", []);
                    var filePath = fileValue.GetString() ?? "";
                    var rawVpath = vpathValue.GetString() ?? "";
                    var normalizedVpath = rawVpath.StartsWith('/') ? rawVpath[1..] : rawVpath;
                    if (!PathRules.IsSafeArchiveRelativePath(filePath) ||
                        !PathRules.IsSafeArchiveRelativePath(normalizedVpath))
                        return new(false, "Field-JSON asset contains an unsafe path.", []);
                    var targetKey = "asset:" + normalizedVpath.ToLowerInvariant();
                    if (!uniqueKeys.Add(targetKey))
                        return new(false, "Field-JSON supplies the same virtual asset more than once.", []);
                    claims.Add(new("asset", targetKey, filePath, false));
                }
                else if (kind.Equals("paloc", StringComparison.OrdinalIgnoreCase))
                {
                    if (!target.TryGetProperty("language", out var locale) ||
                        locale.ValueKind != JsonValueKind.String ||
                        string.IsNullOrWhiteSpace(locale.GetString()) ||
                        !target.TryGetProperty("entries", out var entries) ||
                        entries.ValueKind != JsonValueKind.Array ||
                        entries.GetArrayLength() is < 1 or > MaxClaims)
                        return new(false, "Field-JSON localization target is incomplete.", []);
                    foreach (var value in entries.EnumerateArray())
                    {
                        if (value.ValueKind != JsonValueKind.Object ||
                            !value.TryGetProperty("key", out var localizationKey) ||
                            localizationKey.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(localizationKey.GetString()) ||
                            !value.TryGetProperty("value", out var localizedValue))
                            return new(false, "Field-JSON localization entry is incomplete.", []);
                        var category = value.TryGetProperty("category", out var categoryValue)
                            ? categoryValue.ToString() : "0";
                        var targetKey = "paloc:" + locale.GetString()!.ToLowerInvariant() + ":" +
                            category + ":" + localizationKey.GetString()!.ToLowerInvariant();
                        if (!uniqueKeys.Add(targetKey))
                            return new(false, "Field-JSON has a duplicate localization entry.", []);
                        var digest = Convert.ToHexString(SHA256.HashData(
                            Encoding.UTF8.GetBytes(localizedValue.GetRawText())));
                        claims.Add(new("paloc", targetKey, digest, false));
                        if (claims.Count > MaxClaims)
                            return new(false, "Field-JSON exceeds the bounded mutation count.", []);
                    }
                }
                else
                    return new(false, "Field-JSON uses an unknown target kind; manual review is required.", []);
            }
            return claims.Count == 0
                ? new(false, "Field-JSON has no recognized operations or assets.", [])
                : new(true, "Recognized declarative Field-JSON; runtime compatibility and merging remain unverified.", claims);
        }
        catch (JsonException)
        {
            return new(false, "Field-JSON is malformed or exceeds the supported JSON nesting depth.", []);
        }
    }

    /// <summary>
    /// O(total claims) advisory collision discovery. Does not choose a winning
    /// mod or prove the compiled game archives can accept either change.
    /// </summary>
    public static IReadOnlyList<string> FindClaimCollisions(
        IEnumerable<CrimsonDesertFieldInspection> inspections)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(inspections);
        var seen = new Dictionary<string, CrimsonDesertFieldClaim>(StringComparer.OrdinalIgnoreCase);
        var collisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inspection in inspections)
        {
            if (!inspection.IsValid) continue;
            foreach (var claim in inspection.Claims)
            {
                if (seen.TryGetValue(claim.TargetKey, out var earlier) &&
                    (!claim.IsSimpleSet || !earlier.IsSimpleSet ||
                     !claim.ValueFingerprint.Equals(earlier.ValueFingerprint, StringComparison.OrdinalIgnoreCase)))
                    collisions.Add(claim.TargetKey);
                else
                    seen.TryAdd(claim.TargetKey, claim);
            }
        }
        return collisions.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
