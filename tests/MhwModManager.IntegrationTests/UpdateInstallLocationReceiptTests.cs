using System.Text.Json;
using MhwModManager.Updater;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class UpdateInstallLocationReceiptTests : IDisposable
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private readonly string root = Path.Combine(Path.GetTempPath(),
        "mhwmm-install-location-" + Guid.NewGuid().ToString("N"));
    private string Local => Path.Combine(root, "local");
    private string Updater => Path.Combine(Local, "MhwModManager", "Updater");
    private string Locator => Path.Combine(Updater, "install-location.json");
    private const string Exe = "MHW Mod Manager.exe";
    private const string Dll = "MHW Mod Manager.dll";

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Fact]
    public async Task Ready_package_records_minimal_local_locator_and_replaces_it_on_move()
    {
        var first = Path.Combine(root, "first");
        var identity = await PackageAsync(first);
        Assert.True(await UpdateInstallLocationReceipt.TryRecordAsync(first, Local, identity, TestToken));
        await AssertLocatorAsync(first);

        var second = Path.Combine(root, "second");
        Directory.Move(first, second);
        Assert.True(await UpdateInstallLocationReceipt.TryRecordAsync(second, Local, identity, TestToken));
        await AssertLocatorAsync(second);
        Assert.Single(Directory.EnumerateFiles(Updater));
    }

    [Fact]
    public async Task Unpackaged_wrong_build_corrupt_marker_and_staging_cannot_replace_locator()
    {
        var install = Path.Combine(root, "valid");
        var identity = await PackageAsync(install);
        Assert.True(await UpdateInstallLocationReceipt.TryRecordAsync(install, Local, identity, TestToken));
        var before = await File.ReadAllBytesAsync(Locator, TestToken);

        var unpackaged = Path.Combine(root, "unpackaged");
        Directory.CreateDirectory(unpackaged);
        Assert.False(await UpdateInstallLocationReceipt.TryRecordAsync(unpackaged, Local, identity, TestToken));
        Assert.False(await UpdateInstallLocationReceipt.TryRecordAsync(
            install, Local, identity with { BuildNumber = identity.BuildNumber + 1 }, TestToken));
        await File.WriteAllTextAsync(Path.Combine(install, UpdateProtocol.ProductManifestFileName),
            "{}", TestToken);
        Assert.False(await UpdateInstallLocationReceipt.TryRecordAsync(install, Local, identity, TestToken));

        var staging = Path.Combine(Updater, "staging", "fixture", "payload");
        var staged = await PackageAsync(staging);
        Assert.False(await UpdateInstallLocationReceipt.TryRecordAsync(staging, Local, staged, TestToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(Locator, TestToken));
    }

    [Fact]
    public async Task Cancelled_record_does_not_create_locator()
    {
        var install = Path.Combine(root, "valid");
        var identity = await PackageAsync(install);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            UpdateInstallLocationReceipt.TryRecordAsync(install, Local, identity, cancellation.Token));
        Assert.False(File.Exists(Locator));
    }

    private async Task<UpdateBuildIdentity> PackageAsync(string install)
    {
        Directory.CreateDirectory(Local);
        Directory.CreateDirectory(install);
        var identity = new UpdateBuildIdentity(1, UpdateProtocol.Channel, "8.8.143",
            new string('a', 40), 643, DateTimeOffset.UtcNow);
        await File.WriteAllTextAsync(Path.Combine(install, Exe), "exe-fixture", TestToken);
        await File.WriteAllTextAsync(Path.Combine(install, Dll), "dll-fixture", TestToken);
        await WriteJsonAsync(Path.Combine(install, UpdateProtocol.BuildIdentityFileName), identity);
        var entries = new List<ProductFileEntry>();
        foreach (var name in new[] { Exe, Dll, UpdateProtocol.BuildIdentityFileName })
        {
            var file = Path.Combine(install, name);
            entries.Add(new ProductFileEntry(name, new FileInfo(file).Length,
                await UpdatePackageVerifier.HashFileAsync(file, TestToken)));
        }
        var manifest = Path.Combine(install, UpdateProtocol.ProductManifestFileName);
        await WriteJsonAsync(manifest, new ProductFileManifest(1, entries));
        var marker = new ReleaseInstallMarker(1, UpdateProtocol.ProductId, UpdateProtocol.Channel,
            identity, Exe, await UpdatePackageVerifier.HashFileAsync(manifest, TestToken));
        await WriteJsonAsync(Path.Combine(install, UpdateProtocol.InstallMarkerFileName), marker);
        return identity;
    }

    private async Task AssertLocatorAsync(string install)
    {
        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(Locator, TestToken));
        var value = doc.RootElement;
        Assert.Equal(3, value.EnumerateObject().Count());
        Assert.Equal(1, value.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(UpdateProtocol.ProductId, value.GetProperty("productId").GetString());
        Assert.Equal(install, value.GetProperty("installRoot").GetString());
    }

    private static Task WriteJsonAsync<T>(string path, T value) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, UpdateProtocol.Json), TestToken);
}
