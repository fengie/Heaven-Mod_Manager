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
    private static readonly HashSet<string> KnownOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "add", "scale", "list_append", "list_extend", "list_union",
        "list_set", "list_remove", "list_merge", "array_append",
        "clone_record", "new_record", "delete_record"
    };

    public static CrimsonDesertFieldInspection Inspect(string json)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > MaxUtf8Bytes)
            return new(false, "Field-JSON is empty or exceeds the bounded 256 KiB inspection limit.", []);

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false });
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
            {
                // DMM multi-target entries use "file". A nested "target" is
                // silently skipped by DMM, so fail review rather than claim success.
                if (targetArray.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Object
                    && item.TryGetProperty("target", out _)))
                    return new(false, "DMM multi-target entries require file, not target.", []);
                targets.AddRange(targetArray.EnumerateArray());
            }
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
                        var opName = operation.TryGetProperty("op", out var opElement)
                            ? (opElement.ValueKind == JsonValueKind.String ? opElement.GetString() : null)
                            : "set";
                        if (opName is null || !KnownOperations.Contains(opName))
                            return new(false, "Field-JSON contains an unrecognized operation.", []);

                        var createsRecord = opName.Equals("clone_record", StringComparison.OrdinalIgnoreCase)
                            || opName.Equals("new_record", StringComparison.OrdinalIgnoreCase);
                        if (createsRecord && (!operation.TryGetProperty("new_key", out var created)
                            || created.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)))
                            return new(false, "Field-JSON record creation needs a new_key.", []);
                        if (opName.Equals("clone_record", StringComparison.OrdinalIgnoreCase)
                            && (!operation.TryGetProperty("source_key", out var source)
                                || source.ValueKind is not (JsonValueKind.String or JsonValueKind.Number)))
                            return new(false, "Field-JSON clone needs a source_key.", []);
                        if (opName.Equals("new_record", StringComparison.OrdinalIgnoreCase)
                            && !operation.TryGetProperty("template", out _))
                            return new(false, "Field-JSON new record needs a template.", []);

                        // DMM resolves entry first, key second; using numeric key
                        // when entry is present causes false independent records after
                        // a game update re-keys the same author-named record.
                        string? row = createsRecord ? operation.GetProperty("new_key").ToString() : null;
                        if (row is null && operation.TryGetProperty("entry", out var entry) &&
                            entry.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(entry.GetString()))
                            row = entry.GetString();
                        if (row is null && operation.TryGetProperty("key", out var key) &&
                            key.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                            row = key.ToString();
                        if (row is null && operation.TryGetProperty("match", out var match) &&
                            match.ValueKind == JsonValueKind.Object && match.EnumerateObject().Any())
                            row = "*"; // selector can span any number of unknown records
                        if (string.IsNullOrWhiteSpace(row) || row.Length > 256 ||
                            (row != "*" && row.Contains(':')))
                            return new(false, "Field-JSON operation needs a bounded record key, entry or match.", []);

                        var isSimpleSet = opName.Equals("set", StringComparison.OrdinalIgnoreCase) && row != "*";
                        var needsField = !createsRecord && !opName.Equals("delete_record", StringComparison.OrdinalIgnoreCase);
                        var fieldName = operation.TryGetProperty("field", out var field) &&
                            field.ValueKind == JsonValueKind.String ? field.GetString() : null;
                        if (needsField && (string.IsNullOrWhiteSpace(fieldName) ||
                            fieldName.Length > 256 || fieldName.Contains(':')))
                            return new(false, "Field-JSON field operation needs a bounded field name.", []);
                        var hasValue = operation.TryGetProperty("value", out var newValue)
                            || operation.TryGetProperty("new", out newValue);
                        if ((opName.Equals("set", StringComparison.OrdinalIgnoreCase)
                                || opName.Equals("add", StringComparison.OrdinalIgnoreCase)
                                || opName.StartsWith("list_", StringComparison.OrdinalIgnoreCase)
                                    && !opName.Equals("list_remove", StringComparison.OrdinalIgnoreCase)
                                || opName.Equals("array_append", StringComparison.OrdinalIgnoreCase))
                            && !hasValue)
                            return new(false, "Field-JSON field operation needs a new value.", []);
                        if (opName.Equals("scale", StringComparison.OrdinalIgnoreCase)
                            && (!operation.TryGetProperty("factor", out var factor)
                                || factor.ValueKind != JsonValueKind.Number))
                            return new(false, "Field-JSON scale needs a numeric factor.", []);
                        if (opName.Equals("list_remove", StringComparison.OrdinalIgnoreCase)
                            && !operation.TryGetProperty("where", out _))
                            return new(false, "Field-JSON list_remove needs a where selector.", []);

                        // Non-set and table-wide match operations remain advisory;
                        // never claim the runtime will compose them without resolving
                        // native table data and loader version.
                        var targetKey = "table:" + tableKey + ":" + row.ToLowerInvariant() +
                            ":" + (isSimpleSet ? fieldName!.ToLowerInvariant() : "*");
                        if (!uniqueKeys.Add(targetKey) && isSimpleSet)
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
                    // Normalize slash spelling only after removing the one documented
                    // virtual-root slash. A second leading slash, empty segment or
                    // trailing whitespace remains ambiguous and needs review.
                    var normalizedVpath = (rawVpath.StartsWith('/') ? rawVpath[1..] : rawVpath).Replace('\\', '/');
                    if (!PathRules.IsSafeArchiveRelativePath(filePath) ||
                        !PathRules.IsSafeArchiveRelativePath(normalizedVpath) ||
                        normalizedVpath.Split('/').Any(string.IsNullOrWhiteSpace) ||
                        !string.Equals(normalizedVpath, normalizedVpath.Trim(), StringComparison.Ordinal))
                        return new(false, "Field-JSON asset contains an unsafe or ambiguous path.", []);
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
            return new(false, "Field-JSON is malformed, contains duplicate properties or exceeds the supported JSON nesting depth.", []);
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
        var recordWide = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableWildcard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tableObserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nestedFieldPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var collisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inspection in inspections)
        {
            if (!inspection.IsValid) continue;
            foreach (var claim in inspection.Claims)
            {
                // A record-wide edit (delete/new/clone/list mutation) may
                // overlap any field edit of that same record. Index record
                // identity once; don't quadratically compare sibling fields.
                if (claim.Kind.Equals("table", StringComparison.OrdinalIgnoreCase))
                {
                    var split = claim.TargetKey.LastIndexOf(':');
                    if (split > "table:".Length)
                    {
                        var recordKey = claim.TargetKey[..split];
                        var tableBoundary = recordKey.LastIndexOf(':');
                        if (tableBoundary > "table:".Length)
                        {
                            var tableKey = recordKey[..tableBoundary];
                            if (recordKey.EndsWith(":*", StringComparison.Ordinal))
                            {
                                if (tableObserved.Contains(tableKey))
                                    collisions.Add(claim.TargetKey);
                                tableWildcard.Add(tableKey);
                            }
                            else if (tableWildcard.Contains(tableKey))
                                collisions.Add(claim.TargetKey);
                            tableObserved.Add(tableKey);
                        }
                        if (claim.IsSimpleSet)
                        {
                            if (recordWide.Contains(recordKey) ||
                                nestedFieldPrefixes.Contains(claim.TargetKey))
                                collisions.Add(claim.TargetKey);
                            // Prefixes catch a whole-field overwrite against a
                            // nested field in either order (cooltime vs cooltime.a).
                            var field = claim.TargetKey[(split + 1)..];
                            for (var i = 1; i < field.Length; i++)
                            {
                                if (field[i] is not ('.' or '[')) continue;
                                var ancestor = recordKey + ":" + field[..i];
                                if (seen.ContainsKey(ancestor))
                                    collisions.Add(claim.TargetKey);
                                nestedFieldPrefixes.Add(ancestor);
                            }
                            recordFields.Add(recordKey);
                        }
                        else
                        {
                            if (recordFields.Contains(recordKey))
                                collisions.Add(claim.TargetKey);
                            recordWide.Add(recordKey);
                        }
                    }
                }
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
