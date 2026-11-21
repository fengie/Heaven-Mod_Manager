using System.IO.Compression;
using System.Threading.Channels;
using MhwModManager.App.ViewModels;
using MhwModManager.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class WatchedFolderReconnectTests
{
    [Fact]
    public async Task FilesDepositedOfflineAreReconciledOnceAndFutureEventsResume()
    {
        var root = Directory.CreateTempSubdirectory("intake-reconnect-");
        var source = Path.Combine(root.FullName, "download");
        var offline = Path.Combine(root.FullName, "disconnected");
        var home = Path.Combine(root.FullName, "manager");
        var game = GameProfile.MonsterHunterWorld(Path.Combine(root.FullName, "game"));
        var sorter = new GameModIntakeService(home, Path.Combine(home, "State"), () => [game]);
        var notices = Channel.CreateUnbounded<string?>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        stop.CancelAfter(TimeSpan.FromSeconds(20));
        var ct = stop.Token;
        var interval = TimeSpan.FromMilliseconds(100);
        Task watcher = Task.CompletedTask;
        try
        {
            Directory.CreateDirectory(source);
            sorter.AddSource(source);
            Directory.Move(source, offline);
            watcher = MainWindowViewModel.WatchIntakeFolderAsync(source, interval, name => notices.Writer.TryWrite(name), ct);
            for (int connection = 1; connection <= 2; connection++)
            {
                var name = $"MHW outfit {connection}.zip";
                using (var zip = ZipFile.Open(Path.Combine(offline, name), ZipArchiveMode.Create))
                using (var entry = new StreamWriter(zip.CreateEntry("nativePC/pl/armor/model.tex").Open()))
                    entry.Write("fixture " + connection);
                var original = File.ReadAllBytes(Path.Combine(offline, name));
                Directory.Move(offline, source);
                Assert.Null(await notices.Reader.ReadAsync(ct));
                var routed = await sorter.ScanAsync(ct);
                Assert.Equal(1, routed.Routed);
                Assert.Equal(connection - 1, routed.AlreadyPresent);
                Assert.Equal(original, File.ReadAllBytes(Path.Combine(source, name)));
                var inbox = Directory.GetFiles(Path.Combine(home, "Mods by Game", game.Id, "Inbox"), "*.zip");
                Assert.Equal(connection, inbox.Length);
                Assert.Single(inbox, item => File.ReadAllBytes(item).SequenceEqual(original));
                await Task.Delay(interval * 4, ct);
                Assert.False(notices.Reader.TryRead(out _)); // Healthy roots do not repeatedly rescan package contents.
                var marker = $"after-return-{connection}.txt";
                File.WriteAllText(Path.Combine(source, marker), "live event");
                Assert.Equal(marker, await notices.Reader.ReadAsync(ct));
                if (connection == 1)
                {
                    Directory.Move(source, offline);
                    await Task.Delay(interval * 4, ct);
                    while (notices.Reader.TryRead(out _)) { }
                }
            }
            await stop.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watcher);
            while (notices.Reader.TryRead(out _)) { }
            File.WriteAllText(Path.Combine(source, "after-stop.txt"), "not monitored");
            await Task.Delay(interval * 2, TestContext.Current.CancellationToken);
            Assert.False(notices.Reader.TryRead(out _));
        }
        finally
        {
            await stop.CancelAsync();
            try { await watcher; } catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            root.Delete(true);
        }
    }
}
