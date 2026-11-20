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
    public async Task EveryDiscoveredGameRunsItsOwnInstalledModLifecycle()
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
                await ExerciseGameWindowAsync(game, passed, TestToken);
            }
            catch (Exception ex) when (!TestToken.IsCancellationRequested)
            {
                error = ex.Message;
                failures.Add($"{game.Id} after {passed.LastOrDefault() ?? "startup"}: {error}");
            }
            var evidence = Environment.GetEnvironmentVariable("MHW_GAME_UI_EVIDENCE");
            if (!string.IsNullOrEmpty(evidence))
                File.WriteAllText(Path.Combine(evidence, $"game-{game.Id}.json"),
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

    private static async Task ExerciseGameWindowAsync(GameProfile game, List<string> passed, CancellationToken ct)
    {
        using var run = new DisposableWpfRun(seedInbox: false, profile: game);
        SeedProfileInventoryEvidence(run);
        var archiveHash = SHA256.HashData(File.ReadAllBytes(run.ArchivePath));
        var app = run.Launch();
        var movedPrompt = await WaitForElementAsync(() => AutomationElement.RootElement.FindFirst(TreeScope.Children,
            new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id),
                new PropertyCondition(AutomationElement.NameProperty, "Game installation moved"))),
            UiTimeout, "moved game repair prompt", ct);
        Invoke(await WaitForElementAsync(() => FindButton(movedPrompt, "Yes"), UiTimeout, "relink game", ct));
        var picker = await WaitForElementAsync(() => FindFileDialog(app.Id), UiTimeout, "relink executable picker", ct);
        var executable = FindPickerField(picker, false)!;
        ((ValuePattern)executable.GetCurrentPattern(ValuePattern.Pattern)).SetValue(run.FirstGame.ExecutablePath);
        Invoke(await WaitForElementAsync(() => FindButton(picker, "Open", enabled: true), UiTimeout, "open replacement executable", ct));
        var window = await WaitForMainWindowAsync(app, ct);
        passed.Add("Repair moved installation through native executable picker");
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
        Assert.Equal(ToggleState.Off, ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        passed.Add("Discover Inbox archive in Mods");

        var search = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Edit &&
            e.Current.Name == "Search mods"), UiTimeout, "Search mods", ct);
        var query = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern);
        query.SetValue("absent_fixture_74812");
        await WaitForAsync(() => FindModToggle(window, name) is null, UiTimeout, "filter excludes package", ct);
        query.SetValue("Inbox Fixture");
        toggle = await WaitForElementAsync(() => FindModToggle(window, name), UiTimeout, "filter returns package", ct);
        passed.Add("Filter mod list");
        ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Toggle();

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
        await Filter("Pending", true);
        await Filter("All", true);
        passed.Add("All eight mod filters show or exclude the known staged package");

        async Task Click(string label) => Invoke(await WaitForElementAsync(() => FindButton(window, label, enabled: true),
            UiTimeout, label, ct));
        await Click("Preview Mod Changes");
        await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Text &&
            e.Current.Name.Contains("Dry run passed", StringComparison.OrdinalIgnoreCase) &&
            e.Current.Name.Contains("1 add", StringComparison.OrdinalIgnoreCase)), UiTimeout, "one addition in preview", ct);
        Assert.False(File.Exists(run.LiveModPath));
        passed.Add("Toggle mod and preview one addition");
        await Click("Apply Changes");
        await WaitForAsync(() => File.Exists(run.LiveModPath) && File.ReadAllBytes(run.LiveModPath).SequenceEqual(run.ModBytes),
            UiTimeout, "correct deployed bytes", ct);
        passed.Add("Apply and read deployed file");

        await SelectModsTabAsync(window, ct);
        await Click("Undo Last Applied Change");
        await WaitForAsync(() => !File.Exists(run.LiveModPath), UiTimeout, "undo removes managed payload", ct);
        passed.Add("Undo deployment");
        await Click("Auto Populate");
        await WaitForAsync(() => File.Exists(run.LiveModPath) && File.ReadAllBytes(run.LiveModPath).SequenceEqual(run.ModBytes),
            UiTimeout, "Auto Populate deploys to this profile", ct);
        passed.Add("Auto Populate and read deployed file");
        if (game.SteamAppId is not null) Assert.Equal(run.ModBytes, File.ReadAllBytes(run.WorkshopPayload));
        Assert.Equal(archiveHash, SHA256.HashData(File.ReadAllBytes(run.ArchivePath)));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        Assert.False(File.Exists(Path.Combine(run.SecondGame.LiveModRoot, Path.GetFileName(run.LiveModPath))));
        passed.Add("Preserve source, unmanaged file and other game");

        await ExerciseProfileActionsAsync(game, run, window, passed, ct);
        await InspectGameTabsAsync(game, window, ct);
        passed.Add("Open each main tab and inspect accessible controls");
        await Click("Launch Game");
        await WaitForAsync(() => File.Exists(run.LaunchReceipt) && File.ReadAllLines(run.LaunchReceipt).Length == 3,
            UiTimeout, "fixture game launch receipt", ct);
        var launched = File.ReadAllLines(run.LaunchReceipt);
        Assert.True(PathRules.Comparer.Equals(run.FirstGame.ExecutablePath, launched[0]), "Unexpected game executable.");
        Assert.True(PathRules.Comparer.Equals(run.FirstGame.GameRoot, launched[1]), "Unexpected working directory.");
        using var process = System.Diagnostics.Process.GetProcessById(int.Parse(launched[2], System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Text &&
                e.Current.Name.Contains("survived the startup observation window", StringComparison.OrdinalIgnoreCase)),
                UiTimeout, "launch health for this profile", ct);
        }
        finally
        {
            File.WriteAllText(run.LaunchReceipt + ".done", "");
        }
        await process.WaitForExitAsync(ct);
        passed.Add("Launch registered fixture and verify process path and startup");
    }
}
