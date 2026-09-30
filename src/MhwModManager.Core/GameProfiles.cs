using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace MhwModManager.Core;

public enum GameSupportTier
{
    GenericFolder,
    AdapterEnhanced
}

/// <summary>
/// One isolated game workspace. GenericFolder support is deliberately universal and conservative:
/// the manager knows where a game's executable and managed mod root live, then reuses the same
/// transactional deployment/rollback/conflict machinery without inventing game-specific semantics.
/// AdapterEnhanced profiles may opt into additional knowledge such as MHW asset bundles/coverage.
/// </summary>
public sealed record GameProfile(
    string Id,
    string DisplayName,
    string GameRoot,
    string ExecutableRelativePath,
    string ProcessName,
    string AdapterId,
    string ModRootRelativePath,
    GameSupportTier SupportTier = GameSupportTier.GenericFolder,
    string? NexusGameDomain = null,
    string? SavePath = null,
    bool SupportsSemanticCoverage = false,
    string? SteamAppId = null,
    string? Store = null,
    int? GameBananaGameId = null)
{
    [JsonIgnore] public string ExecutablePath => Path.GetFullPath(Path.Combine(GameRoot, ExecutableRelativePath));
    [JsonIgnore] public string LiveModRoot => string.IsNullOrWhiteSpace(ModRootRelativePath) ? Path.GetFullPath(GameRoot) : Path.GetFullPath(Path.Combine(GameRoot, ModRootRelativePath));
    [JsonIgnore] public bool IsMonsterHunterWorld => AdapterId.Equals("mhw", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool HasNexusIntegration => !string.IsNullOrWhiteSpace(NexusGameDomain);
    [JsonIgnore] public bool HasGameBananaIntegration => GameBananaGameId is > 0;
    [JsonIgnore] public string ManagedDescription => string.IsNullOrWhiteSpace(ModRootRelativePath) ? "Game root" : ModRootRelativePath;
    [JsonIgnore] public string StorageKey
    {
        get
        {
            var seed=(Id+"|"+Path.GetFullPath(GameRoot)).ToLowerInvariant();
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)).AsSpan(0,6)).ToLowerInvariant();
            return NormalizeId(Id)+"-"+hash;
        }
    }

    public static GameProfile MonsterHunterWorld(string root) => new(
        "monster-hunter-world",
        "Monster Hunter: World",
        Path.GetFullPath(root),
        "MonsterHunterWorld.exe",
        "MonsterHunterWorld",
        "mhw",
        "nativePC",
        GameSupportTier.AdapterEnhanced,
        "monsterhunterworld",
        null,
        true,
        "582010",
        "Steam",
        9081);

    public static GameProfile Generic(
        string id,
        string displayName,
        string root,
        string executableRelativePath,
        string modRootRelativePath,
        string adapterId="generic-folder",
        string? steamAppId=null,
        string? nexusGameDomain=null,
        string? store=null,
        int? gameBananaGameId=null) => new(
        NormalizeId(id),
        string.IsNullOrWhiteSpace(displayName) ? Path.GetFileNameWithoutExtension(executableRelativePath) : displayName.Trim(),
        Path.GetFullPath(root),
        NormalizeRelative(executableRelativePath, allowEmpty:false),
        Path.GetFileNameWithoutExtension(executableRelativePath),
        string.IsNullOrWhiteSpace(adapterId)?"generic-folder":adapterId.Trim(),
        NormalizeRelative(modRootRelativePath, allowEmpty:true),
        string.Equals(adapterId,"generic-folder",StringComparison.OrdinalIgnoreCase)?GameSupportTier.GenericFolder:GameSupportTier.AdapterEnhanced,
        string.IsNullOrWhiteSpace(nexusGameDomain)?null:nexusGameDomain.Trim().Trim('/'),
        null,
        false,
        string.IsNullOrWhiteSpace(steamAppId)?null:steamAppId.Trim(),
        string.IsNullOrWhiteSpace(store)?null:store.Trim(),
        gameBananaGameId is > 0 ? gameBananaGameId : null);

    public static string NormalizeId(string value)
    {
        var chars=(value??string.Empty).Trim().ToLowerInvariant().Select(ch=>char.IsLetterOrDigit(ch)?ch:'-').ToArray();
        var collapsed=string.Join('-',new string(chars).Split('-',StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(collapsed)?"game":collapsed;
    }

    public static bool IsCanonicalId(string? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(string.IsNullOrWhiteSpace(value)||value.Contains('\\')||value.Contains('/')||Path.IsPathRooted(value))return false;
        return value.Equals(NormalizeId(value),StringComparison.Ordinal);
    }

    /// <summary>Validates a path relative to the game root without applying managed-file namespaces.</summary>
    public static string NormalizeRelative(string value,bool allowEmpty)
    {
        var raw=(value??string.Empty).Trim().Replace('/','\\').Trim('\\');
        if(string.IsNullOrWhiteSpace(raw))
        {
            if(allowEmpty)return string.Empty;
            throw new ArgumentException("A relative path is required.",nameof(value));
        }
        if(Path.IsPathRooted(raw)||raw.Contains(':'))throw new ArgumentException("Game profile paths must be relative to the selected game root.",nameof(value));
        var segments=raw.Split('\\',StringSplitOptions.RemoveEmptyEntries);
        if(segments.Length==0||segments.Any(x=>x is "." or ".."||string.IsNullOrWhiteSpace(x)))throw new ArgumentException("Game profile paths cannot contain traversal segments.",nameof(value));
        foreach(var segment in segments)
        {
            if(segment.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Game profile path contains invalid characters.",nameof(value));
            if(!segment.Equals(segment.TrimEnd('.', ' '),StringComparison.Ordinal))throw new ArgumentException("Game profile path contains an unsafe segment.",nameof(value));
        }
        return string.Join('\\',segments);
    }
}
