using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public Task EveryDiscoveredGameRunsItsOwnInstalledModLifecycle() =>
        VerifyAllGamesAsync("game", ExerciseGameWindowAsync);

    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public Task EveryDiscoveredGameCanRelinkItsMovedExecutable() =>
        VerifyAllGamesAsync("repair", ExerciseRepairAsync);

    private static async Task VerifyAllGamesAsync(string scenario,
        Func<GameProfile, List<string>, CancellationToken, Task> exercise)
    {
        var games = DiscoveredGameProfiles.Load();
        Assert.NotEmpty(games);
        var failures = new List<string>();
        var failedOperations = new HashSet<(string LastSuccess, string Error)>();
        // Test one of each adapter early so a common fixture failure is distinguishable
        // from a single game's behavior. A green run must still exercise every profile.
        var byAdapter = games.GroupBy(game => game.AdapterId).ToArray();
        var ordered = byAdapter.Select(group => group.First())
            .Concat(byAdapter.SelectMany(group => group.Skip(1))).ToArray();
        foreach (var game in ordered)
        {
            TestToken.ThrowIfCancellationRequested();
            var passed = new List<string>();
            string? error = null;
            try
            {
                await exercise(game, passed, TestToken);
            }
            catch (Exception ex) when (!TestToken.IsCancellationRequested)
            {
                error = ex.Message;
                failures.Add($"{game.Id} after {passed.LastOrDefault() ?? "startup"}: {error}");
            }
            var evidence = Environment.GetEnvironmentVariable("MHW_GAME_UI_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
                File.WriteAllText(Path.Combine(evidence, $"{scenario}-{game.Id}.json"),
                    JsonSerializer.Serialize(new { game.Id, game.AdapterId, passed, error }));
            if (error is not null && !failedOperations.Add((passed.LastOrDefault() ?? "startup", error)))
            {
                failures.Add("The same operation failed twice. Remaining profiles require a corrected candidate.");
                break;
            }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static async Task InspectGameTabsAsync(GameProfile game, AutomationElement window, CancellationToken ct)
    {
        var main = await WaitForElementAsync(() => Find(window, e => e.Current.AutomationId == "MainTabs"),
            UiTimeout, "main tab navigation", ct);
        var tabs = main.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem))
            .Cast<AutomationElement>().ToArray();
        var names = tabs.Select(tab => tab.Current.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[] { "Dashboard", "Mods", game.SupportsSemanticCoverage ? "Outfits" : "Coverage",
            "Needs attention", "Profiles", "History", "Shared Files", "Settings", "Browse Mods", "Installed files" })
            Assert.Contains(name, names);

        var tabReports = new Dictionary<string, object>();
        var actionTypes = new[] { ControlType.Button, ControlType.CheckBox, ControlType.RadioButton,
            ControlType.ComboBox, ControlType.Edit, ControlType.Hyperlink, ControlType.MenuItem };
        var filter = new OrCondition(actionTypes.Select(type => (Condition)new PropertyCondition(
            AutomationElement.ControlTypeProperty, type)).ToArray());
        foreach (var tab in tabs)
        {
            ct.ThrowIfCancellationRequested();
            var selection = (SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern);
            selection.Select();
            await WaitForAsync(() => selection.Current.IsSelected, UiTimeout, tab.Current.Name, ct);
            var cache = new CacheRequest();
            cache.Add(AutomationElement.AutomationIdProperty);
            cache.Add(AutomationElement.NameProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            cache.Add(AutomationElement.IsEnabledProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            cache.Add(AutomationElement.IsInvokePatternAvailableProperty);
            using (cache.Activate())
            {
                var controls = main.FindAll(TreeScope.Descendants, filter).Cast<AutomationElement>()
                    .Select(e => new { name = e.Cached.Name, id = e.Cached.AutomationId,
                        type = e.Cached.ControlType.ProgrammaticName, enabled = e.Cached.IsEnabled,
                        offscreen = e.Cached.IsOffscreen,
                        supportsInvoke = (bool)e.GetCachedPropertyValue(AutomationElement.IsInvokePatternAvailableProperty) })
                    .ToArray();
                tabReports.Add(tab.Current.Name, controls);
            }
        }
        var evidence = Environment.GetEnvironmentVariable("MHW_GAME_UI_EVIDENCE");
        if (!string.IsNullOrEmpty(evidence))
            File.WriteAllText(Path.Combine(evidence, $"controls-{game.Id}.json"), JsonSerializer.Serialize(tabReports));
    }

    private static async Task ExerciseRepairAsync(GameProfile game, List<string> passed, CancellationToken ct)
    {
        using var run = new DisposableWpfRun(seedInbox: false, profile: game, moved: true);
        var app = run.Launch();
        AutomationElement? FindRepairPrompt()
        {
            if (app.HasExited) throw new InvalidOperationException($"Startup exited with {app.ExitCode} before repair.");
            var roots = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id));
            foreach (AutomationElement root in roots)
            {
                // Windows may expose an owned message box under the splash rather than at desktop level.
                var found = Find(root, element => element.Current.Name == "Game installation moved" &&
                    (element.Current.ControlType == ControlType.Window || element.Current.ClassName == "#32770"));
                if (found is not null) return found;
            }
            return null;
        }
        AutomationElement prompt;
        try
        {
            prompt = await WaitForElementAsync(FindRepairPrompt, UiTimeout, "moved game repair prompt", ct);
        }
        catch (TimeoutException)
        {
            var evidence = Environment.GetEnvironmentVariable("MHW_GAME_UI_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence) && !app.HasExited)
            {
                var roots = AutomationElement.RootElement.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id));
                var windows = roots.Cast<AutomationElement>().Take(8)
                    .Select(e => new { e.Current.Name, e.Current.ClassName, type = e.Current.ControlType.ProgrammaticName });
                File.WriteAllText(Path.Combine(evidence, $"windows-{game.Id}.json"), JsonSerializer.Serialize(windows));
            }
            throw;
        }
        passed.Add("Recognize unavailable registered executable");
        var yes = await WaitForElementAsync(() => FindButton(prompt, "Yes"), UiTimeout, "relink game", ct);
        var confirmation = Task.Run(() => Invoke(yes), ct);
        var picker = await WaitForElementAsync(() => FindFileDialog(app.Id), UiTimeout, "relink executable picker", ct);
        var executable = FindPickerField(picker, false)!;
        ((ValuePattern)executable.GetCurrentPattern(ValuePattern.Pattern)).SetValue(run.FirstGame.ExecutablePath);
        Invoke(await WaitForElementAsync(() => FindButton(picker, "Open", enabled: true),
            UiTimeout, "open replacement executable", ct));
        await confirmation.WaitAsync(UiTimeout, ct);
        var window = await WaitForMainWindowAsync(app, ct);
        await WaitForElementAsync(() => Find(window, e => e.Current.AutomationId == "ActiveGameSelector"),
            UiTimeout, "relinked game selector", ct);
        Assert.Equal(game.DisplayName, ReadActiveGame(window));
        var repaired = run.RegisteredGame();
        Assert.Equal(run.FirstGame.GameRoot, repaired.GameRoot);
        Assert.Equal(run.FirstGame.LiveModRoot, repaired.LiveModRoot);
        passed.Add("Relink executable and verify saved installation and mod directory");
    }

    private static async Task ExerciseGameWindowAsync(GameProfile game, List<string> passed, CancellationToken ct)
    {
        using var run = new DisposableWpfRun(seedInbox: false, profile: game);
        SeedProfileInventoryEvidence(run);
        var archiveHash = SHA256.HashData(File.ReadAllBytes(run.ArchivePath));
        var app = run.Launch();
        var window = await WaitForMainWindowAsync(app, ct);
        await WaitForElementAsync(() => Find(window, e => e.Current.AutomationId == "ActiveGameSelector"),
            UiTimeout, "active game selector", ct);
        await WaitForAsync(() => ReadActiveGame(window) == game.DisplayName, UiTimeout, "active profile " + game.Id, ct);
        passed.Add("Open registered profile");

        await SelectModsTabAsync(window, ct);
        var externalName = game.SteamAppId is not null ? "新皮肤 Workshop fixture" :
            game.AdapterId == "bepinex" ? "Example plugin" : null;
        if (externalName is not null)
        {
            var grid = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.DataGrid &&
                e.Current.Name == "Mod library"), UiTimeout, "discovered library", ct);
            var title = await WaitForElementAsync(() => Find(grid, e => e.Current.Name == externalName),
                UiTimeout, "external mod visible on Mods", ct);
            var row = title;
            while (row is not null && row.Current.ControlType != ControlType.DataItem)
                row = TreeWalker.ControlViewWalker.GetParent(row);
            Assert.NotNull(row);
            Assert.Null(Find(row, e => e.Current.ControlType == ControlType.CheckBox));
            Assert.False(FindButton(window, "Select all visible mods")!.Current.IsEnabled);
            passed.Add("Discover externally owned mod without a deployment toggle");
        }
        run.CopyToInbox(run.ArchivePath);
        const string name = "E2E Inbox Fixture";
        var toggle = await WaitForElementAsync(() => FindModToggle(window, name), UiTimeout, "Inbox mod in Mods", ct);
        await WaitForAsync(()=>File.Exists(run.LiveModPath)&&File.ReadAllBytes(run.LiveModPath).SequenceEqual(run.ModBytes), UiTimeout,"Inbox data automatically installed under this game",ct);
        passed.Add("Discover Inbox archive in Mods");

        var search = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Edit &&
            e.Current.Name == "Search mods"), UiTimeout, "Search mods", ct);
        var query = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern);
        query.SetValue("absent_fixture_74812");
        await WaitForAsync(() => FindModToggle(window, name) is null, UiTimeout, "filter excludes package", ct);
        query.SetValue("Inbox Fixture");
        toggle = await WaitForElementAsync(() => FindModToggle(window, name), UiTimeout, "filter returns package", ct);
        passed.Add("Filter mod list");

        async Task Filter(string label, bool visible)
        {
            var filter = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Button &&
                e.Current.Name.StartsWith(label + " (", StringComparison.Ordinal)), UiTimeout, label + " filter", ct);
            Invoke(filter);
            await WaitForAsync(() => (FindModToggle(window, name) is not null) == visible,
                UiTimeout, label + " filters the staged package", ct);
        }
        foreach (var label in new[] { "Review families", "Updates", "Problems", "Needs check", "Not used" })
        {
            await Filter(label, false);
            await Filter("All", true);
        }
        await Filter("Updates", false);
        await Filter("Enabled", true);
        await Filter("Updates", false);
        await Filter("Pending", false);
        await Filter("All", true);
        passed.Add("All eight mod filters show or exclude the known installed package");

        async Task Click(string label) => Invoke(await WaitForElementAsync(() => FindButton(window, label, enabled: true),
            UiTimeout, label, ct));
        passed.Add("Automatic discovery applied exact game bytes");
        toggle=await WaitForElementAsync(()=>FindModToggle(window,name),UiTimeout,"installed mod",ct);
        ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
        await WaitForAsync(()=>!File.Exists(run.LiveModPath),UiTimeout,"explicit disable removed managed payload",ct);
        passed.Add("Explicit off automatically updates game files");
        await Click("Undo Last Applied Change");
        await WaitForAsync(()=>File.Exists(run.LiveModPath)&&File.ReadAllBytes(run.LiveModPath).SequenceEqual(run.ModBytes),UiTimeout,"Undo restored the explicit switch",ct);
        passed.Add("Undo deployment");
        if (game.SteamAppId is not null) Assert.Equal(run.ModBytes, File.ReadAllBytes(run.WorkshopPayload));
        Assert.Equal(archiveHash, SHA256.HashData(File.ReadAllBytes(run.ArchivePath)));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        Assert.False(File.Exists(Path.Combine(run.SecondGame.LiveModRoot, Path.GetFileName(run.LiveModPath))));
        passed.Add("Preserve source, unmanaged file and other game");

        await ExerciseProfileActionsAsync(game, run, window, passed, ct);
        await InspectGameTabsAsync(game, window, ct);
        passed.Add("Open each main tab and inspect accessible controls");
        var dashboard = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.TabItem &&
            e.Current.Name == "Dashboard"), UiTimeout, "Dashboard launch status", ct);
        ((SelectionItemPattern)dashboard.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        await ExerciseWatchedFolderPickerAsync(run, window, passed, ct);
        await Click("Launch Game");
        await WaitForAsync(() => File.Exists(run.LaunchReceipt) && File.ReadAllLines(run.LaunchReceipt).Length == 3,
            UiTimeout, "fixture game launch receipt", ct);
        var launched = File.ReadAllLines(run.LaunchReceipt);
        Assert.True(PathRules.Comparer.Equals(run.FirstGame.ExecutablePath, launched[0]), "Unexpected game executable.");
        Assert.True(PathRules.Comparer.Equals(run.FirstGame.GameRoot, launched[1]), "Unexpected working directory.");
        using var process = System.Diagnostics.Process.GetProcessById(int.Parse(launched[2], System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            // The launch journal also survives a subsequent status message or tab change.
            var connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(Path.GetDirectoryName(run.SettingsPath)!, "manager.db"),
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            connection.Open();
            using var verifiedLaunch = connection.CreateCommand();
            verifiedLaunch.CommandText = "SELECT COUNT(*) FROM launch_history WHERE success=1 AND startup_survived=1";
            await WaitForAsync(() => (long)verifiedLaunch.ExecuteScalar()! == 1,
                UiTimeout, "committed successful observed launch for this profile", ct);
        }
        finally
        {
            File.WriteAllText(run.LaunchReceipt + ".done", "");
        }
        await process.WaitForExitAsync(ct);
        passed.Add("Launch registered fixture and verify process path and startup");

        // A saved enable that has never been applied must survive vanilla play as a
        // choice, without being silently deployed when the game exits.
        var db = run.OpenCatalog();
        try
        {
            var waiting = Assert.Single(await db.GetModsAsync(ct), item => item.DisplayName == "Independent Verification");
            Assert.False(waiting.Enabled);
            var waitingPath = Path.Combine(Path.GetDirectoryName(run.LiveModPath)!, "independent-" + Path.GetFileName(run.LiveModPath));
            Assert.False(File.Exists(waitingPath));
            await db.SetEnabledAndPriorityAsync(new Dictionary<string, (bool, int)> { [waiting.Id] = (true, waiting.Priority) }, ct);
            File.Delete(run.LaunchReceipt);
            File.Delete(run.LaunchReceipt + ".done");
            await Click("Launch Without Mods");
            try
            {
                await WaitForAsync(() => File.Exists(run.LaunchReceipt) && File.ReadAllLines(run.LaunchReceipt).Length == 3,
                    UiTimeout, "vanilla game launched", ct);
                Assert.Equal(run.FirstGame.ExecutablePath, File.ReadAllLines(run.LaunchReceipt)[0], ignoreCase: true);
                Assert.False(File.Exists(run.LiveModPath));
                Assert.False(File.Exists(waitingPath));
                Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
            }
            finally { File.WriteAllText(run.LaunchReceipt + ".done", ""); }
            await WaitForAsync(() => File.Exists(run.LiveModPath) && File.ReadAllBytes(run.LiveModPath).SequenceEqual(run.ModBytes),
                UiTimeout, "previously applied payload restored after vanilla exit", ct);
            await using var readback = await db.OpenAsync(ct);
            await using var committed = readback.CreateCommand();
            committed.CommandText = "SELECT COUNT(*) FROM operations WHERE state='Committed' AND description='Restore after safe mode'";
            await WaitForAsync(() => (long)committed.ExecuteScalar()! == 1, UiTimeout, "vanilla restore committed", ct);
            Assert.False(File.Exists(waitingPath));
            Assert.True(Assert.Single(await db.GetModsAsync(ct), item => item.Id == waiting.Id).Enabled);
            Assert.Equal(archiveHash, SHA256.HashData(File.ReadAllBytes(run.ArchivePath)));
            passed.Add("Launch Without Mods removes the deployed payload for play and restores only the prior deployment");
        }
        finally
        {
            await using var connection = await db.OpenAsync(CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
        await ExerciseAddGameAsync(run, window, passed, ct);
    }

    private static async Task ExerciseWatchedFolderPickerAsync(DisposableWpfRun run, AutomationElement window,
        List<string> passed, CancellationToken ct)
    {
        var parent = Path.Combine(Path.GetDirectoryName(run.ManagerHome)!, "watch sources");
        var target = Directory.CreateDirectory(Path.Combine(parent, "watch-" + Guid.NewGuid().ToString("N")));
        var keep = Path.Combine(target.FullName, "keep.txt");
        File.WriteAllText(keep, "owned fixture");
        var fieldName = "Add automatic mod sorting folder";
        var add = await WaitForElementAsync(() => FindButton(window, fieldName, enabled: true), UiTimeout, fieldName, ct);
        var opening = Task.Run(() => Invoke(add), ct);
        var picker = await WaitForElementAsync(() => FindFileDialog(window.Current.ProcessId, folder: true),
            UiTimeout, "watched folder picker", ct);
        var filename = await WaitForElementAsync(() => FindPickerField(picker, true), UiTimeout, "folder name", ct);
        filename.SetFocus();
        // A filename edit can show a path while the shell still has Documents selected.
        // Navigate via the native address bar and select the unique fixture item itself.
        dynamic keys = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        try
        {
            Assert.Equal(window.Current.ProcessId, AutomationElement.FocusedElement.Current.ProcessId);
            keys.SendKeys("^l");
            var address = await WaitForElementAsync(() =>
            {
                var focused = AutomationElement.FocusedElement;
                return focused.Current.ProcessId == window.Current.ProcessId &&
                    focused.Current.ControlType == ControlType.Edit && !System.Windows.Automation.Automation.Compare(focused, filename)
                    ? focused : null;
            }, UiTimeout, "native folder address field", ct);
            await TypeNativePathAsync(address, parent, window.Current.ProcessId, ct);
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject((object)keys); }
        var choice = await WaitForElementAsync(() => Find(picker, e => e.Current.Name == target.Name &&
            e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _)), UiTimeout, "unique watched folder in picker", ct);
        ((SelectionItemPattern)choice.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Invoke(await WaitForElementAsync(() => FindButton(picker, "Select Folder", enabled: true),
            UiTimeout, "confirm selected watched folder", ct));
        await opening.WaitAsync(UiTimeout, ct);
        var state = Path.Combine(run.ManagerHome, "State", "mod-intake-folders.json");
        bool Watching() => File.Exists(state) && (JsonSerializer.Deserialize<string[]>(File.ReadAllText(state)) ?? [])
            .Contains(target.FullName, StringComparer.OrdinalIgnoreCase);
        await WaitForAsync(Watching, UiTimeout, "persisted selected watched folder", ct);
        if (run.FirstGame.IsMonsterHunterWorld)
        {
            await WaitForElementAsync(() => FindButton(window, fieldName, enabled: true), UiTimeout, "initial watch scan finished", ct);
            // Allow the initial four-second event debounce to finish before replacing the watched root.
            await Task.Delay(TimeSpan.FromSeconds(6), ct);
            var replacement = Directory.CreateDirectory(Path.Combine(parent, "replacement"));
            replacement.CreationTimeUtc = DateTime.UtcNow.AddDays(-2);
            var archiveName = "MHW Reconnect Fixture.zip";
            var archive = Path.Combine(replacement.FullName, archiveName);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            using (var payload = zip.CreateEntry("nativePC/pl/fixture-reconnect.bin").Open())
                payload.Write(run.ModBytes);
            var hash = SHA256.HashData(File.ReadAllBytes(archive));
            File.Copy(keep, Path.Combine(replacement.FullName, "keep.txt"));
            var detached = Path.Combine(parent, "detached");
            Directory.Move(target.FullName, detached);
            Directory.Move(replacement.FullName, target.FullName);
            await SelectModsTabAsync(window, ct);
            var installed = Path.Combine(run.FirstGame.LiveModRoot, "pl", "fixture-reconnect.bin");
            await WaitForAsync(() => FindModToggle(window, "MHW Reconnect Fixture") is { } mod &&
                ((TogglePattern)mod.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On &&
                File.Exists(installed) && File.ReadAllBytes(installed).SequenceEqual(run.ModBytes),
                UiTimeout, "reconnected folder automatically installs the verified game payload", ct);
            Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(Path.Combine(target.FullName, archiveName))));
            Assert.Equal("owned fixture", File.ReadAllText(Path.Combine(detached, "keep.txt")));
            var dashboard = await WaitForElementAsync(() => Find(window, item =>
                item.Current.ControlType == ControlType.TabItem && item.Current.Name == "Dashboard"), UiTimeout, "Dashboard", ct);
            ((SelectionItemPattern)dashboard.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            passed.Add("Automatically import a package deposited while a watched folder was disconnected and retain both originals");
        }
        Invoke(await WaitForElementAsync(() => FindButton(window, "Stop monitoring selected mod folder without deleting data",
            enabled: true), UiTimeout, "remove watched fixture", ct));
        await WaitForAsync(() => !Watching(), UiTimeout, "remove folder from monitoring", ct);
        Assert.Equal("owned fixture", File.ReadAllText(keep));
        passed.Add("Select the watched folder through the native chooser, verify its saved path and remove it without deleting the source");
    }
}
