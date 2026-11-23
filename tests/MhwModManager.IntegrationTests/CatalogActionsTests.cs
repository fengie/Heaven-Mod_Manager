using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.App;
using MhwModManager.App.ViewModels;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class CatalogActionsTests
{
    [Fact]
    public Task ModsEnterSelectsFilteredResultsWithoutStagingAcrossRegisteredProfiles() =>
        IsolatedWpfSession.RunAsync((services, ct) =>
        {
            foreach (var game in DiscoveredGameProfiles.Load())
            {
                ct.ThrowIfCancellationRequested();
                var paths = services.Paths with { Game = DiscoveredGameProfiles.Isolate(game, services.Paths.ToolRoot) };
                using var vm = new MainWindowViewModel(services with { Paths = paths }, new HttpClient(new NoNetwork()));
                var rows = (System.Collections.IList)vm.LibraryView.SourceCollection;
                var mod = new ModDescriptor("managed", "Managed", "Managed", paths.ModsRoot, false, 0);
                var managed = new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([mod])), () => { });
                var external = new MhwModManager.Filesystem.ExternalLibraryReference("142", "Shared outfit", "Workshop", []);
                var second = external with { ItemId = "143" };
                rows.Add(managed);
                rows.Add(external);
                rows.Add(second);
                var (field, enter) = EnterBinding(vm, "SearchText");
                var changes = 0;
                vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.SelectedLibraryItem)) changes++; };
                field.Text = "Shared outfit";
                enter.Execute(null);
                Assert.Equal(2, vm.VisibleLibraryCount);
                Assert.Same(external, vm.SelectedLibraryItem);
                field.Text = "143";
                enter.Execute(null);
                Assert.Same(second, vm.SelectedLibraryItem);
                field.Text = "Managed";
                enter.Execute(null);
                Assert.Same(managed, vm.SelectedLibraryItem);
                field.Text = "absent-fixture";
                enter.Execute(null);
                Assert.Null(vm.SelectedLibraryItem);
                Assert.Equal(0, vm.VisibleLibraryCount);
                Assert.Equal(4, changes);
                Assert.False(managed.StagedEnabled);
                Assert.False(vm.HasStagedChanges);
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task AllRegisteredProfilesSearchTheirOwnCacheAndClearWithoutContactingAProvider() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var repository = new CatalogRepository(services.Database);
            await repository.UpsertSourceAsync("Fixture", CatalogComplianceFixture.Active("fixture"), ct);
            var games = DiscoveredGameProfiles.Load();
            var stored = games.SelectMany(game => new[] { Item(game, "Crimson"), Item(game, "Azure") }).ToArray();
            await repository.UpsertBatchAsync(stored.Select(mod => (Cached(mod), true)).ToArray(), ct);
            foreach (var game in games)
            {
                var paths = services.Paths with { Game = DiscoveredGameProfiles.Isolate(game, services.Paths.ToolRoot) };
                var transport = new NoNetwork();
                using var vm = new MainWindowViewModel(services with { Paths = paths, StartupArguments = ["--offline-catalog"] }, new HttpClient(transport));
                await vm.RefreshCatalogCommand.ExecuteAsync(null);
                Assert.Equal(2, vm.CatalogItems.Count);
                Assert.All(vm.CatalogItems, row => Assert.Equal(game.Id, row.Mod.GameId));
                Assert.Equal(Visibility.Collapsed, vm.CatalogLoadMoreVisibility);
                vm.CatalogQuery = "Crimson";
                await Assert.IsAssignableFrom<IAsyncRelayCommand>(EnterBinding(vm, "CatalogQuery").Command).ExecuteAsync(null);
                Assert.Equal(Item(game, "Crimson").CanonicalId, Assert.Single(vm.CatalogItems).Mod.CanonicalId);
                Assert.Contains("cached", vm.CatalogProviderSummary, StringComparison.OrdinalIgnoreCase);
                vm.CatalogQuery = "no-such-catalog-fixture";
                await vm.SearchCatalogCommand.ExecuteAsync(null);
                Assert.Empty(vm.CatalogItems);
                await vm.ClearCatalogSearchCommand.ExecuteAsync(null);
                Assert.Empty(vm.CatalogQuery);
                Assert.Equal(0, transport.Requests);
                Assert.Equal(2, vm.CatalogItems.Count);
            }
        });

    [Fact]
    public Task SharedBrowseCommandsPageLocalResultsAndRecoverProviderFailure() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var game = services.Paths.Game;
            var repository = new CatalogRepository(services.Database);
            await repository.UpsertSourceAsync("Fixture", CatalogComplianceFixture.Active("fixture"), ct);
            var items = Enumerable.Range(0, 1001).Select(i => Cached(Item(game, $"Cache {i:D4}"))).ToArray();
            await repository.UpsertBatchAsync(items.Select(item => (item,true)).ToArray(), ct);
            var first = Item(game, "Amber");
            var second = Item(game, "Jade");
            var last = Item(game, "Topaz");
            var provider = new CatalogSyncServiceTests.FakeProvider {
                SearchException = new HttpRequestException("offline fixture"),
                SearchResults = [first],
                BrowsePageFactory = cursor => cursor switch {
                    null => new([first], "next"), "next" => new([second], "last"), _ => new([last])
                }
            };
            using var vm = new MainWindowViewModel(services, new HttpClient(new NoNetwork()), [provider]);
            await vm.RefreshCatalogCommand.ExecuteAsync(null);
            Assert.Equal(1000, vm.CatalogItems.Count);
            Assert.Equal(Visibility.Visible, vm.CatalogProviderRetryVisibility);
            Assert.Equal(Visibility.Visible, vm.CatalogLoadMoreCachedVisibility);
            var before = provider.BrowsePageCalls;
            await vm.LoadMoreCachedCatalogCommand.ExecuteAsync(null);
            Assert.Equal(before, provider.BrowsePageCalls);
            Assert.Equal(1001, vm.CatalogItems.Select(row => row.Mod.CanonicalId).Distinct().Count());
            Assert.Equal(Visibility.Collapsed, vm.CatalogLoadMoreCachedVisibility);
            provider.SearchException = null;
            await vm.RefreshCatalogCommand.ExecuteAsync(null);
            Assert.Equal(Visibility.Collapsed, vm.CatalogProviderRetryVisibility);
            Assert.Equal(Visibility.Visible, vm.CatalogLoadMoreVisibility);
            await vm.LoadMoreCatalogCommand.ExecuteAsync(null);
            Assert.Equal("next", provider.LastCursor);
            Assert.NotNull(await repository.GetAsync(second.CanonicalId, ct));
            await vm.IndexAllAvailableModsCommand.ExecuteAsync(null);
            Assert.Equal("last", provider.LastCursor);
            Assert.NotNull(await repository.GetAsync(last.CanonicalId, ct));
            Assert.Equal(Visibility.Collapsed, vm.CatalogLoadMoreVisibility);
            vm.CatalogQuery = "Topaz";
            for (var tick = 0; tick < 100 && (vm.CatalogItems.Count != 1 || vm.CatalogItems[0].Mod.CanonicalId != last.CanonicalId); tick++)
                await Task.Delay(25, ct);
            Assert.Equal(last.CanonicalId, Assert.Single(vm.CatalogItems).Mod.CanonicalId);
            vm.CatalogQuery = "Amber";
            await Assert.IsAssignableFrom<IAsyncRelayCommand>(EnterBinding(vm, "CatalogQuery").Command).ExecuteAsync(null);
            Assert.Equal("Amber", provider.LastRequest?.Query);
            vm.SelectedCatalogItem = Assert.Single(vm.CatalogItems);
            provider.FileException = new HttpRequestException("offline file-list fixture");
            await vm.LoadCatalogFilesCommand.ExecuteAsync(null);
            Assert.Equal(Visibility.Visible, vm.CatalogFileRetryVisibility);
            Assert.Equal(first.CanonicalId, vm.SelectedCatalogItem!.Mod.CanonicalId);
            provider.FileException = null;
            await vm.LoadCatalogFilesCommand.ExecuteAsync(null);
            Assert.Equal("file-1", Assert.Single(vm.CatalogFiles).File.ProviderFileId);
            Assert.Null(vm.SelectedCatalogFile);
            await vm.InstallCatalogFileCommand.ExecuteAsync(null);
            Assert.Empty(await services.Database.GetModsAsync(ct));
            await vm.CheckCatalogUpdatesCommand.ExecuteAsync(null);
            Assert.Empty(vm.CatalogOriginStatuses);
        });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task ExactFileAcquisitionPreservesIdentityAndRejectsUnexpectedSize(bool incorrectSize, bool incorrectHeader) =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var game = services.Paths.Game;
            var mod = Item(game, "Chosen");
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var entry = zip.CreateEntry("Mods/fixture-payload.bin").Open();
                entry.Write([3, 2, 1]);
            }
            var archive = buffer.ToArray();
            var file = new CatalogModFile("fixture",mod.ProviderModId,"exact-id","Chosen file","chosen.zip",
                CatalogFileCategory.Main,"1",archive.Length + (incorrectSize ? 9 : 0));
            var transport = new ArchiveTransport(archive, incorrectHeader);
            using var http = new HttpClient(transport);
            var origins = new InstalledCatalogOriginRepository(services.Database);
            var importer = new CatalogAcquisitionService(http, services.Importer, origins, services.Database,
                services.Hashing, Path.Combine(services.Paths.NextStateRoot, "DownloadTemp"), savedArchiveRoot: services.Paths.DownloadsRoot);
            var provider = new CatalogSyncServiceTests.FakeProvider();
            var assisted = await importer.AcquireAsync(provider,game,mod,file,ct);
            Assert.Equal(CatalogImportOutcomeKind.Assisted, assisted.Kind);
            Assert.Equal(new Uri(mod.SourceUrl), assisted.AssistedUri);
            Assert.Equal(0, transport.Requests);
            Assert.Empty(await origins.GetAllAsync(ct));
            provider.Acquisition = new(CatalogAcquisitionKind.Direct, new Uri("https://fixture.example.test/chosen.zip"), null, null, "fixture");
            await Assert.ThrowsAsync<InvalidDataException>(() => importer.AcquireAsync(provider,game,mod,file with { ProviderModId="other" },ct));
            Assert.Equal(0, transport.Requests);
            if (incorrectSize || incorrectHeader)
            {
                await Assert.ThrowsAsync<InvalidDataException>(() => importer.AcquireAsync(provider,game,mod,file,ct));
                Assert.Empty(await services.Database.GetModsAsync(ct));
                Assert.Empty(await origins.GetAllAsync(ct));
                Assert.Empty(Directory.GetFiles(Path.Combine(services.Paths.NextStateRoot, "DownloadTemp"), "catalog-*"));
                Assert.Empty(Directory.GetFiles(services.Paths.DownloadsRoot, "*", SearchOption.AllDirectories));
                return;
            }
            var outcome = await importer.AcquireAsync(provider,game,mod,file,ct);
            Assert.Equal(CatalogImportOutcomeKind.Imported, outcome.Kind);
            var origin = Assert.Single(await origins.GetAllAsync(ct));
            Assert.Equal(file.ProviderFileId, origin.ProviderFileId);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(archive)), origin.ArchiveSha256, ignoreCase: true);
            var recorded = Assert.Single(await services.Database.GetModsAsync(ct));
            Assert.Equal(recorded.Id, origin.ModId);
            Assert.False(recorded.Enabled);
            Assert.Equal(1, transport.Requests);
        });

    [Fact]
    public Task Catalog_download_preflight_refuses_known_payload_before_staging_when_disk_space_is_insufficient() =>
        IsolatedWpfSession.RunAsync(async (services, ct) =>
        {
            var game = services.Paths.Game;
            var mod = Item(game, "Capacity guard");
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var entry = zip.CreateEntry("Mods/fixture.bin").Open();
                entry.Write([1, 2, 3]);
            }

            var archive = buffer.ToArray();
            var file = new CatalogModFile("fixture", mod.ProviderModId, "capacity-file",
                "Capacity guard archive", "chosen.zip", CatalogFileCategory.Main, "1", archive.Length);
            var provider = new CatalogSyncServiceTests.FakeProvider
            {
                Acquisition = new(CatalogAcquisitionKind.Direct,
                    new Uri("https://fixture.example.test/chosen.zip"), null, null, "fixture")
            };
            var transport = new ArchiveTransport(archive, incorrectHeader: false);
            using var http = new HttpClient(transport);
            var origins = new InstalledCatalogOriginRepository(services.Database);
            var stagingRoot = Path.Combine(services.Paths.NextStateRoot, "DownloadTemp");
            var importer = new CatalogAcquisitionService(
                http, services.Importer, origins, services.Database, services.Hashing,
                stagingRoot, savedArchiveRoot: services.Paths.DownloadsRoot,
                availableSpaceProvider: _ => 0);

            var error = await Assert.ThrowsAsync<IOException>(
                () => importer.AcquireAsync(provider, game, mod, file, ct));

            Assert.Contains("Not enough free disk space", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, transport.Requests);
            Assert.Empty(await services.Database.GetModsAsync(ct));
            Assert.Empty(await origins.GetAllAsync(ct));
            Assert.Empty(Directory.GetFiles(stagingRoot, "catalog-*"));
            Assert.Empty(Directory.GetFiles(services.Paths.DownloadsRoot, "*", SearchOption.AllDirectories));
        });

    private sealed class ArchiveTransport(byte[] bytes, bool incorrectHeader) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("https://fixture.example.test/chosen.zip",request.RequestUri!.AbsoluteUri);
            Requests++;
            var content = new ByteArrayContent(bytes);
            if (incorrectHeader) content.Headers.ContentLength = bytes.Length + 3;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage=request, Content=content });
        }
    }

    private static CachedCatalogMod Cached(CatalogMod mod) => new(mod, new(DateTimeOffset.UtcNow));
    private static CatalogMod Item(GameProfile game, string name) =>
        CatalogSyncServiceTests.CreateMod("fixture", game.Id + "-" + name.Replace(' ', '-')) with { GameId = game.Id, Name = name };

    private static (TextBox Field, ICommand Command) EnterBinding(MainWindowViewModel vm, string property)
    {
        var path = new DirectoryInfo(AppContext.BaseDirectory);
        while (path is not null && !File.Exists(Path.Combine(path.FullName, "MhwModManager.sln"))) path = path.Parent;
        var source = XDocument.Load(Path.Combine(path!.FullName, "src", "MhwModManager.App", "MainWindow.xaml"));
        var input = source.Descendants().Single(e => e.Name.LocalName == "TextBox" &&
            ((string?)e.Attribute("Text"))?.StartsWith("{Binding " + property + ",", StringComparison.Ordinal) == true);
        var field = (TextBox)XamlReader.Parse(input.ToString());
        field.DataContext = vm;
        var enter = Assert.Single(field.InputBindings.OfType<KeyBinding>());
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        Assert.Equal(Key.Enter, enter.Key);
        Assert.Equal(ModifierKeys.None, enter.Modifiers);
        return (field, Assert.IsAssignableFrom<ICommand>(enter.Command));
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            throw new InvalidOperationException("Unexpected external request from isolated catalog test");
        }
    }
}
