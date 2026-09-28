using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Core;

namespace MhwModManager.Updater;

public sealed class GitHubUpdateSource(HttpClient httpClient, Action<string>? log = null)
{
    private const string ApiBase = "https://api.github.com";
    private readonly HttpClient http = httpClient;
    private readonly Action<string> writeLog = log ?? (_ => { });

    public async Task<UpdateCandidate?> FindLatestAsync(UpdateBuildIdentity current, string token, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"current={current.BuildNumber}");
        if (string.IsNullOrWhiteSpace(token)) throw new UnauthorizedAccessException("GitHub updater credential is not configured.");
        using var request = CreateRequest(HttpMethod.Get, $"{ApiBase}/repos/{UpdateProtocol.Repository}/releases?per_page=100", token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseDto>>(stream, UpdateProtocol.Json, ct) ?? [];
        var release = releases
            .Where(r => !r.Draft && !r.Prerelease && r.Immutable)
            .Select(r => (Release: r, Build: ParseBuildNumber(r.TagName)))
            .Where(x => x.Build > current.BuildNumber)
            .OrderByDescending(x => x.Build)
            .FirstOrDefault();
        if (release.Release is null) return null;

        var manifestAsset = release.Release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, "update-manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Release {release.Release.TagName} is missing update-manifest.json.");
        if (manifestAsset.Size <= 0 || manifestAsset.Size > 64 * 1024)
            throw new InvalidDataException($"Release manifest size {manifestAsset.Size} is outside the allowed budget.");

        var manifestBytes = await DownloadBytesAsync(new Uri(manifestAsset.ApiUrl), token, 64 * 1024, ct);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestBytes, UpdateProtocol.Json)
                       ?? throw new InvalidDataException("Update manifest is empty.");
        manifest.Validate();
        if (manifest.BuildNumber != release.Build)
            throw new InvalidDataException($"Release tag build {release.Build} does not match manifest build {manifest.BuildNumber}.");
        var artifact = release.Release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, manifest.ArtifactName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Release is missing declared artifact '{manifest.ArtifactName}'.");
        if (artifact.Size != manifest.ArtifactSize)
            throw new InvalidDataException($"Release asset size {artifact.Size} does not match manifest size {manifest.ArtifactSize}.");
        writeLog($"update remote build={manifest.BuildNumber} sha={manifest.SourceSha}");
        return new UpdateCandidate(manifest, new Uri(artifact.ApiUrl), new Uri(manifestAsset.ApiUrl));
    }

    public async Task DownloadArtifactAsync(UpdateCandidate candidate, string token, string destination, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"build={candidate.Manifest.BuildNumber}");
        var manifest = candidate.Manifest;
        manifest.Validate();
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("Download destination has no parent.");
        Directory.CreateDirectory(parent);
        using var request = CreateRequest(HttpMethod.Get, candidate.ArtifactApiUri.ToString(), token);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        if (response.Content.Headers.ContentLength is long declared && declared != manifest.ArtifactSize)
            throw new InvalidDataException($"Download content length {declared} does not match expected {manifest.ArtifactSize}.");

        var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            long total = 0;
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;
                    total += read;
                    if (total > manifest.ArtifactSize || total > UpdateProtocol.MaxArtifactBytes)
                        throw new InvalidDataException("Update download exceeded the declared/resource size budget.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            await output.FlushAsync(ct);
            output.Flush(true);
            if (total != manifest.ArtifactSize)
                throw new InvalidDataException($"Update download was truncated: expected {manifest.ArtifactSize}, received {total}.");
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!string.Equals(actual, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Update SHA-256 mismatch. Expected {manifest.Sha256}, actual {actual}.");
            await output.DisposeAsync();
            File.Move(temp, destination, false);
            writeLog($"update download verified build={manifest.BuildNumber} bytes={total} sha256={actual}");
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string uri, string token)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"method={method}");
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var requestUri))
            throw new InvalidDataException("GitHub updater API URI is not absolute.");
        ValidateAuthenticatedApiUri(requestUri);
        var request = new HttpRequestMessage(method, requestUri);
        request.Headers.UserAgent.ParseAdd("MHW-Manual-Mod-Manager-Updater/1");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static void ValidateAuthenticatedApiUri(Uri uri)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"host={uri.Host}");
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.IdnHost, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException(
                "Authenticated updater requests are restricted to https://api.github.com.");
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, string token, int maxBytes, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"maxBytes={maxBytes}");
        using var request = CreateRequest(HttpMethod.Get, uri.ToString(), token);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccessAsync(response, ct);
        if (response.Content.Headers.ContentLength is long length && length > maxBytes)
            throw new InvalidDataException($"Update metadata exceeded {maxBytes} bytes.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            if (memory.Length + read > maxBytes) throw new InvalidDataException($"Update metadata exceeded {maxBytes} bytes.");
            await memory.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        return memory.ToArray();
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"status={(int)response.StatusCode}");
        if (response.IsSuccessStatusCode) return;
        var detail = response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden
            ? "GitHub updater authentication failed."
            : $"GitHub updater request failed with HTTP {(int)response.StatusCode}.";
        _ = await response.Content.ReadAsStringAsync(ct);
        throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new UnauthorizedAccessException(detail)
            : new HttpRequestException(detail, null, response.StatusCode);
    }

    private static long ParseBuildNumber(string tag)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"tag={tag}");
        const string prefix = "updater-main-";
        if (!tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return -1;
        return long.TryParse(tag[prefix.Length..], out var number) ? number : -1;
    }
}
