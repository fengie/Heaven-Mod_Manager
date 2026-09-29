using System.Net;
using System.Net.Sockets;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class RemotePreviewNetworkTrustTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mhwmm-network-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    public RemotePreviewNetworkTrustTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch { }
    }

    [Theory]
    [InlineData("http")]
    [InlineData("https")]
    public async Task Sidecar_local_preview_is_rejected_before_connection(string scheme)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var acceptTask = listener.AcceptTcpClientAsync(TestToken).AsTask();

        var state = Path.Combine(root, "state-" + scheme);
        var source = Path.Combine(root, "mod-" + scheme);
        Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(
            Path.Combine(source, "mod-manager.meta.json"),
            $$"""{"previewUrl":"{{scheme}}://127.0.0.1:{{port}}/preview.png"}""",
            TestToken);

        var db = new ManagerDatabase(Path.Combine(state, "manager.db"));
        await db.InitializeAsync(TestToken);
        await db.UpsertModAsync(
            new ModDescriptor("m-" + scheme, "m", "Local Preview", source, true, 1),
            TestToken);

        var service = new NexusMetadataService(db, new PlannerSnapshotRepository(db), state);
        await service.RefreshAsync(TestToken).WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        var first = await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromMilliseconds(750), TestToken));
        Assert.NotSame(acceptTask, first);
    }

    [Fact]
    public void Remote_preview_transport_disables_redirects_and_proxy_bypass()
    {
        using var preview = NexusMetadataService.CreatePreviewHandler();
        using var nexus = NexusMetadataService.CreateNexusHandler();

        Assert.False(preview.AllowAutoRedirect);
        Assert.False(preview.UseProxy);
        Assert.NotNull(preview.ConnectCallback);
        Assert.False(nexus.AllowAutoRedirect);
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("ff02::1", false)]
    [InlineData("::ffff:127.0.0.1", false)]
    [InlineData("1.1.1.1", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void Remote_preview_address_policy_rejects_non_public_ranges(string value, bool expected)
    {
        Assert.Equal(expected, NexusMetadataService.IsPublicNetworkAddress(IPAddress.Parse(value)));
    }
}
