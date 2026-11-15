using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace MhwModManager.Core;

/// <summary>
/// Optional read-only navigation to the public Heaven.gg research portal.
/// Website metadata is NEVER a source of game-profile, mod, installation,
/// updater or executable authority. A caller must explicitly request a link.
/// </summary>
public static class HeavenGameHubCatalog
{
    public const string CatalogAddress = "https://fengie.github.io/Heaven.gg/games.json";
    public const string WebsiteAddress = "https://fengie.github.io/Heaven.gg/";
    private const int MaxCatalogBytes = 16384;
    private static readonly Uri CatalogUri = new(CatalogAddress, UriKind.Absolute);
    private static readonly Uri WebsiteUri = new(WebsiteAddress, UriKind.Absolute);

    /// <summary>
    /// Looks up a public page for a supported existing game ID. A caller-supplied
    /// HttpClient makes the bounded network boundary testable. The production
    /// caller disables automatic redirects and cookies.
    /// </summary>
    public static async Task<Uri?> FindGuideAsync(
        string? gameId, HttpClient client, CancellationToken cancellationToken = default)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(client);

        // Unknown local games must not be advertised as supported by the site.
        if (!GameProfile.IsCanonicalId(gameId) || gameId is not
            ("celtic-heroes" or "aion2" or "monster-hunter-world"))
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, CatalogUri);
        using var response = await client.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK ||
            response.Content.Headers.ContentLength is > MaxCatalogBytes)
            return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var bounded = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (bounded.Length + read > MaxCatalogBytes) return null;
            bounded.Write(chunk, 0, read);
        }

        try
        {
            using var document = JsonDocument.Parse(bounded.ToArray(),
                new JsonDocumentOptions { MaxDepth = 12 });
            return ReadVerifiedGuide(gameId, document.RootElement);
        }
        catch (JsonException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static Uri? ReadVerifiedGuide(string gameId, JsonElement root)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!HasKeys(root, "schema", "repository", "games") ||
            root.GetProperty("schema").GetString() != "heaven-gaming/game-registry/v1" ||
            root.GetProperty("repository").GetString() != "fengie/Heaven.gg")
            return null;

        var games = root.GetProperty("games");
        if (games.ValueKind != JsonValueKind.Array || games.GetArrayLength() != 3)
            return null;

        string[] ids = ["celtic-heroes", "aion2", "monster-hunter-world"];
        string[] names = ["Celtic Heroes", "AION 2", "Monster Hunter: World"];
        string[] routes = ["./#celtic-heroes", "./#aion2", "./#mod-manager"];
        var index = 0;
        foreach (var game in games.EnumerateArray())
        {
            var desktop = index == 2;
            if (!HasKeys(game, desktop
                    ? ["id", "name", "experience", "siteRoute", "companion"]
                    : ["id", "name", "experience", "siteRoute"]) ||
                game.GetProperty("id").GetString() != ids[index] ||
                game.GetProperty("name").GetString() != names[index] ||
                game.GetProperty("experience").GetString() !=
                    (desktop ? "windows-companion" : "research") ||
                game.GetProperty("siteRoute").GetString() != routes[index])
                return null;

            if (desktop)
            {
                var companion = game.GetProperty("companion");
                if (!HasKeys(companion, "repository", "releasePage", "sourcePage",
                        "platform", "integration") ||
                    companion.GetProperty("repository").GetString() !=
                        "fengie/Heaven-Mod_Manager" ||
                    companion.GetProperty("releasePage").GetString() !=
                        "https://github.com/fengie/Heaven-Mod_Manager/releases/latest" ||
                    companion.GetProperty("sourcePage").GetString() !=
                        "https://github.com/fengie/Heaven-Mod_Manager" ||
                    companion.GetProperty("platform").GetString() != "windows-x64" ||
                    companion.GetProperty("integration").GetString() != "download-link-only")
                    return null;
            }
            index++;
        }
        return new Uri(WebsiteUri, routes[Array.IndexOf(ids, gameId)]);
    }

    private static bool HasKeys(JsonElement value, params string[] expected)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (value.ValueKind != JsonValueKind.Object) return false;
        var actual = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!actual.Add(property.Name)) return false;
        }
        return actual.Count == expected.Length && expected.All(actual.Contains);
    }
}
