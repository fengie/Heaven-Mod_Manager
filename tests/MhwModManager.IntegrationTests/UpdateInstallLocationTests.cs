using System.Security.Cryptography;
using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdateInstallLocationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-install-location-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(root, "profile");
    private string Locator => Path.Combine(Local, "MhwModManager", "Updater", "install-location.json");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Published_install_location_is_persisted_without_runtime_or_transaction_state()
    {
        var install = await CreateInstallAsync("portable");
        Assert.True(await UpdateInstallLocationStore.RecordAsync(install, Local, Ct));
        using var record = JsonDocument.Parse(await File.ReadAllTextAsync(Locator, Ct));
        Assert.Equal(1, record.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(UpdateProtocol.ProductId, record.RootElement.GetProperty("productId").GetString());
        Assert.Equal(install, record.RootElement.GetProperty("installRoot").GetString());
        Assert.Equal(3, record.RootElement.EnumerateObject().Count());
        var before = File.GetLastWriteTimeUtc(Locator);
        Assert.True(await UpdateInstallLocationStore.RecordAsync(install, Local, Ct));
        Assert.Equal(before, File.GetLastWriteTimeUtc(Locator));
        var moved = await CreateInstallAsync("moved");
        Assert.True(await UpdateInstallLocationStore.RecordAsync(moved, Local, Ct));
        using var replacement = JsonDocument.Parse(await File.ReadAllTextAsync(Locator, Ct));
        Assert.Equal(moved, replacement.RootElement.GetProperty("installRoot").GetString());
    }

    [Fact]
    public async Task Unpackaged_or_invalid_install_does_not_replace_last_valid_location()
    {
        var install = await CreateInstallAsync("portable");
        Assert.True(await UpdateInstallLocationStore.RecordAsync(install, Local, Ct));
        var before = await File.ReadAllBytesAsync(Locator, Ct);
        var dev = Path.Combine(root, "dev");
        Directory.CreateDirectory(dev);
        Assert.False(await UpdateInstallLocationStore.RecordAsync(dev, Local, Ct));
        var tampered = await CreateInstallAsync("tampered");
        await File.AppendAllTextAsync(Path.Combine(tampered, UpdateProtocol.ProductManifestFileName), "bad", Ct);
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateInstallLocationStore.RecordAsync(tampered, Local, Ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(Locator, Ct));
    }

    [Fact]
    public async Task Cancelled_record_does_not_replace_existing_location()
    {
        var install = await CreateInstallAsync("portable");
        Assert.True(await UpdateInstallLocationStore.RecordAsync(install, Local, Ct));
        var before = await File.ReadAllBytesAsync(Locator, Ct);
        var moved = await CreateInstallAsync("moved");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UpdateInstallLocationStore.RecordAsync(moved, Local, cancelled.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(Locator, Ct));
    }

    private async Task<string> CreateInstallAsync(string name)
    {
        Directory.CreateDirectory(Local);
        var install = Path.Combine(root, name);
        Directory.CreateDirectory(install);
        var build = new UpdateBuildIdentity(1, "main", "8.8.143", new string('a', 40), 643, DateTimeOffset.UtcNow);
        var buildPath = Path.Combine(install, UpdateProtocol.BuildIdentityFileName);
        await File.WriteAllTextAsync(buildPath, JsonSerializer.Serialize(build, UpdateProtocol.Json), Ct);
        var exe = Path.Combine(install, "MHW Mod Manager.exe");
        var dll = Path.Combine(install, "MHW Mod Manager.dll");
        await File.WriteAllTextAsync(exe, "fixture executable", Ct);
        await File.WriteAllTextAsync(dll, "fixture assembly", Ct);
        var entries = new List<ProductFileEntry>();
        foreach (var path in new[] { buildPath, exe, dll })
        {
            var bytes = await File.ReadAllBytesAsync(path, Ct);
            entries.Add(new ProductFileEntry(Path.GetFileName(path), bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
        }
        var manifestPath = Path.Combine(install, UpdateProtocol.ProductManifestFileName);
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new ProductFileManifest(1, entries), UpdateProtocol.Json), Ct);
        var digest = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(manifestPath, Ct)));
        var marker = new ReleaseInstallMarker(1, UpdateProtocol.ProductId, "main", build, "MHW Mod Manager.exe", digest);
        await File.WriteAllTextAsync(Path.Combine(install, UpdateProtocol.InstallMarkerFileName), JsonSerializer.Serialize(marker, UpdateProtocol.Json), Ct);
        return install;
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }
}
