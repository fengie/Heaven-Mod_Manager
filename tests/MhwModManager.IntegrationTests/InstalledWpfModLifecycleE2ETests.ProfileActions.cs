using System.Windows.Automation;
using System.Text.Json;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    private static void SeedProfileInventoryEvidence(DisposableWpfRun run)
    {
        foreach (var fixture in new[] { run.FirstGame, run.SecondGame })
        {
            var folder = Directory.CreateDirectory(Path.Combine(fixture.GameRoot, "Mods", "inventory-test"));
            File.WriteAllText(Path.Combine(folder.FullName, "mod.json"), "{}");
        }
    }

    private static async Task ExerciseProfileActionsAsync(GameProfile game, DisposableWpfRun run, AutomationElement window,
        List<string> passed, CancellationToken ct)
    {
        async Task SelectTab(string name)
        {
            var tab = await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.TabItem &&
                item.Current.Name == name), UiTimeout, name + " tab", ct);
            ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        }

        async Task<AutomationElement> Button(string name) => await WaitForElementAsync(
            () => FindButton(window, name, enabled: true), UiTimeout, name, ct);

        async Task<AutomationElement> Grid(string name) => await WaitForElementAsync(() => Find(window, item =>
            item.Current.ControlType == ControlType.DataGrid && item.Current.Name == name), UiTimeout, name, ct);

        const string fixtureMod = "E2E Inbox Fixture";
        var profile = "Verified setup " + game.Id;
        await SelectTab("Profiles");
        var field = await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.Edit &&
            item.Current.Name == "New profile name"), UiTimeout, "new profile name field", ct);
        field.SetFocus();
        ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue(profile);
        var save = await Button("Save Current Setup");
        save.SetFocus(); // The name binding commits when the edit field loses focus.
        Invoke(save);

        var profiles = await Grid("Saved profiles");
        await WaitForElementAsync(() => Find(profiles, item => item.Current.Name == profile), UiTimeout,
            "saved profile " + game.Id, ct);
        passed.Add("Save active mod setup as a named profile");

        await SelectModsTabAsync(window, ct);
        var mod = await WaitForElementAsync(() => FindModToggle(window, fixtureMod), UiTimeout, "applied mod", ct);
        var toggle = (TogglePattern)mod.GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.On, toggle.Current.ToggleState);
        toggle.Toggle();
        await WaitForAsync(() => toggle.Current.ToggleState == ToggleState.Off, UiTimeout, "staged mod disable", ct);

        await SelectTab("Profiles");
        Invoke(await Button("Refresh"));
        profiles = await Grid("Saved profiles");
        var saved = await WaitForElementAsync(() => Find(profiles, item => item.Current.Name == profile), UiTimeout,
            "refreshed saved profile", ct);
        var row = saved;
        while (row.Current.ControlType != ControlType.DataItem)
            row = TreeWalker.RawViewWalker.GetParent(row) ?? throw new InvalidOperationException("Saved profile has no grid row.");
        ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        Invoke(await Button("Use Selected Profile"));
        await SelectModsTabAsync(window, ct);
        await WaitForAsync(() => FindModToggle(window, fixtureMod) is { } current &&
            ((TogglePattern)current.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState == ToggleState.On,
            UiTimeout, "saved profile restores the staged enabled choice", ct);
        passed.Add("Refresh and use saved profile to restore enabled selection");

        await SelectTab("History");
        Invoke(await Button("Refresh"));
        var history = await Grid("Recent activity");
        await WaitForElementAsync(() => Find(history, item => item.Current.ControlType == ControlType.DataItem &&
            Find(item, cell => cell.Current.Name.Contains("staged mod-state change", StringComparison.Ordinal)) is not null &&
            Find(item, cell => cell.Current.Name == "Committed") is not null), UiTimeout, "committed deployment in history", ct);
        passed.Add("Refresh History and verify the committed deployment");

        await SelectTab("Settings");
        var packageDirectory = Environment.GetEnvironmentVariable("MHW_WPF_E2E_PACKAGE_DIR")!;
        Assert.False(MhwModManager.Updater.UpdateClientService.CanSelfUpdate(packageDirectory));
        foreach (var (label, key) in new[] { ("Interface animations", "uiAnimationsEnabled"),
            ("Remember last tab", "rememberLastTab"), ("Confirm before apply", "confirmBeforeApply"),
            ("Confirm before discard", "confirmBeforeDiscardStaged"), ("Background metadata refresh", "backgroundMetadataRefreshEnabled"),
            ("Automatic program updates", "autoUpdateEnabled") })
        {
            var box = await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.CheckBox &&
                item.Current.Name == label), UiTimeout, label, ct);
            var pattern = (TogglePattern)box.GetCurrentPattern(TogglePattern.Pattern);
            bool Saved()
            {
                using var settings = JsonDocument.Parse(File.ReadAllBytes(run.SettingsPath));
                return settings.RootElement.GetProperty(key).GetBoolean();
            }
            var before = Saved();
            Assert.Equal(before, pattern.Current.ToggleState == ToggleState.On);
            pattern.Toggle();
            await WaitForAsync(() => Saved() != before && (pattern.Current.ToggleState == ToggleState.On) != before,
                UiTimeout, label + " persisted", ct);
            pattern.Toggle();
            await WaitForAsync(() => Saved() == before && (pattern.Current.ToggleState == ToggleState.On) == before,
                UiTimeout, label + " restored", ct);
            passed.Add("Persist and restore " + label);
        }
        Invoke(await Button("Measure manager storage"));
        await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Text &&
            e.Current.Name.StartsWith("Storage totals refreshed", StringComparison.Ordinal)), UiTimeout, "storage measurement", ct);
        passed.Add("Measure manager storage");
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.LiveModPath));

        await SelectTab("Installed files");
        var filter = await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.Edit &&
            item.Current.AutomationId == "LocalModSearch"), UiTimeout, "installed file inventory filter", ct);
        var inventoryQuery = (ValuePattern)filter.GetCurrentPattern(ValuePattern.Pattern);
        var inventory = await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.DataGrid &&
            Find(item, header => header.Current.ControlType == ControlType.HeaderItem && header.Current.Name == "Evidence") is not null),
            UiTimeout, "installed files evidence grid", ct);
        var firstEvidence = Path.Combine(run.FirstGame.GameRoot, "Mods", "inventory-test", "mod.json");
        var secondEvidence = Path.Combine(run.SecondGame.GameRoot, "Mods", "inventory-test", "mod.json");
        foreach (var (expected, excluded) in new[] { (firstEvidence, secondEvidence), (secondEvidence, firstEvidence) })
        {
            inventoryQuery.SetValue(expected);
            await WaitForAsync(() => Find(inventory, cell => cell.Current.Name == expected) is not null &&
                Find(inventory, cell => cell.Current.Name == excluded) is null, UiTimeout, "inventory path filtering", ct);
        }
        inventoryQuery.SetValue("");
        passed.Add("Filter installed file evidence across both registered game folders");

        await SelectTab(game.SupportsSemanticCoverage ? "Outfits" : "Coverage");
        Invoke(await Button("Refresh coverage"));
        if (game.SupportsSemanticCoverage)
        {
            await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.DataItem &&
                Find(item, cell => cell.Current.Name == "Leather") is not null &&
                Find(item, cell => cell.Current.Name == "pl001_0000") is not null),
                UiTimeout, "indexed armor name and model ID in the same row", ct);
            passed.Add("Refresh outfit coverage and verify Leather model pl001_0000");
        }
        else
        {
            await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.Text &&
                item.Current.Name.StartsWith("Detailed game-specific coverage is not available", StringComparison.Ordinal)),
                UiTimeout, "unsupported semantic coverage explanation", ct);
            Assert.Null(Find(window, item => item.Current.ControlType == ControlType.DataItem));
            passed.Add("Refresh generic coverage and verify unsupported semantic rows stay empty");
        }
        await ExerciseFilteredLibraryActionsAsync(run, window, passed, ct);
        await ExerciseWorkflowActionsAsync(run, window, passed, ct);
    }

    private static async Task ExerciseFilteredLibraryActionsAsync(DisposableWpfRun run, AutomationElement window,
        List<string> passed, CancellationToken ct)
    {
        await SelectModsTabAsync(window, ct);
        const string primaryName = "E2E Inbox Fixture";
        const string alternateName = "Independent Verification";
        var originalArchiveHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(run.ArchivePath));
        var archive = Path.Combine(Path.GetDirectoryName(run.ArchivePath)!, alternateName + ".zip");
        var alternateLive = Path.Combine(Path.GetDirectoryName(run.LiveModPath)!, "independent-" + Path.GetFileName(run.LiveModPath));
        var relative = Path.GetRelativePath(run.FirstGame.GameRoot, alternateLive).Replace(Path.DirectorySeparatorChar, '/');
        byte[] payload = [21, 41, 67, 101, 149];
        using (var zip = System.IO.Compression.ZipFile.Open(archive, System.IO.Compression.ZipArchiveMode.Create))
        using (var stream = zip.CreateEntry(relative).Open())
            stream.Write(payload);
        var alternateHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive));
        run.CopyToInbox(archive);

        var search = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Edit &&
            e.Current.Name == "Search mods"), UiTimeout, "filter for independent package", ct);
        var filter = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern);
        filter.SetValue(alternateName);
        ToggleState? ReadToggle(string name) => FindModToggle(window, name) is { } checkbox
            ? ((TogglePattern)checkbox.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState : null;
        await WaitForAsync(() => ReadToggle(alternateName) == ToggleState.Off && ReadToggle(primaryName) is null,
            UiTimeout, "only independent disabled package visible", ct);
        async Task<AutomationElement> Button(string label) => await WaitForElementAsync(
            () => FindButton(window, label, enabled: true), UiTimeout, label, ct);

        // Simulate a missing catalog index for the disposable disabled package without touching either source.
        var db = run.OpenCatalog();
        try
        {
            var mod = Assert.Single(await db.GetModsAsync(ct), item => item.DisplayName == alternateName);
            await db.ReplaceModFilesAsync(mod.Id, [], ct);
            Assert.DoesNotContain(await db.GetModFilesAsync(ct), row => row.ModId == mod.Id);
            Invoke(await Button("Refresh 1 visible mod"));
            var wantHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(payload));
            await WaitForAsync(() => db.GetModFilesAsync(ct).GetAwaiter().GetResult().Any(row =>
                row.ModId == mod.Id && row.BlobSha256 == wantHash && row.Length == payload.Length &&
                Path.GetFileName(row.Path) == Path.GetFileName(alternateLive)),
                UiTimeout, "filtered reindex restores exact missing catalog payload", ct);
        }
        finally
        {
            await using var connection = await db.OpenAsync(CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
        Assert.False(File.Exists(alternateLive));
        passed.Add("Refresh one filtered package and rebuild its missing content index from original bytes");

        int pid = window.Current.ProcessId;
        AutomationElement? Dialog(string title)
        {
            var owned = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
            foreach (AutomationElement root in owned)
            {
                if (root.Current.Name == title) return root;
                var child = root.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
                    new PropertyCondition(AutomationElement.NameProperty, title)));
                if (child is not null) return child;
            }
            return null;
        }
        async Task Answer(string action, string caption, string choice)
        {
            var button = await Button(action);
            var invocation = Task.Run(() => Invoke(button), ct);
            var dialog = await WaitForElementAsync(() => Dialog(caption), UiTimeout, caption, ct);
            var answer = await WaitForElementAsync(() => FindButton(dialog, choice, enabled: true),
                UiTimeout, caption + " " + choice, ct);
            Invoke(answer);
            await invocation.WaitAsync(UiTimeout, ct);
            await WaitForAsync(() => Dialog(caption) is null, UiTimeout, "dismiss " + caption, ct);
        }
        filter.SetValue("");
        await WaitForAsync(() => ReadToggle(primaryName) == ToggleState.On && ReadToggle(alternateName) == ToggleState.Off,
            UiTimeout, "both packages with independent choices", ct);
        await Answer("Select all visible mods", "Enable visible mods", "No");
        Assert.Equal(ToggleState.Off, ReadToggle(alternateName));
        await Answer("Select all visible mods", "Enable visible mods", "Yes");
        await WaitForAsync(() => ReadToggle(primaryName) == ToggleState.On && ReadToggle(alternateName) == ToggleState.On,
            UiTimeout, "both packages staged on", ct);
        await Answer("Deselect all visible mods", "Disable visible mods", "Yes");
        await WaitForAsync(() => ReadToggle(primaryName) == ToggleState.Off && ReadToggle(alternateName) == ToggleState.Off,
            UiTimeout, "both packages staged off", ct);
        await Answer("Discard Pending Changes", "Discard pending changes", "No");
        Assert.Equal(ToggleState.Off, ReadToggle(primaryName));
        await Answer("Discard Pending Changes", "Discard pending changes", "Yes");
        await WaitForAsync(() => ReadToggle(primaryName) == ToggleState.On && ReadToggle(alternateName) == ToggleState.Off,
            UiTimeout, "discard restores independently deployed choices", ct);
        passed.Add("Cancel and confirm bulk selection, deselect both, and cancel and confirm discard");

        const string review = "Existing mod layouts — read-only preview";
        var inspect = await Button("Inspect nested mod package layouts without making changes");
        var inspectInvocation = Task.Run(() => Invoke(inspect), ct);
        var preview = await WaitForElementAsync(() => Dialog(review), UiTimeout, review, ct);
        await WaitForElementAsync(() => Find(preview, item => item.Current.ControlType == ControlType.Text &&
            item.Current.Name.StartsWith("Inspected 2 of 2 managed packages", StringComparison.Ordinal)),
            UiTimeout, "two readable package layouts", ct);
        Invoke(await WaitForElementAsync(() => FindButton(preview, "Close preview", enabled: true), UiTimeout, "Close preview", ct));
        await inspectInvocation.WaitAsync(UiTimeout, ct);
        await WaitForAsync(() => Dialog(review) is null, UiTimeout, "read-only preview closed", ct);
        Assert.Equal(originalArchiveHash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(run.ArchivePath)));
        Assert.Equal(alternateHash, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archive)));
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.LiveModPath));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        Assert.False(File.Exists(alternateLive));
        passed.Add("Inspect two package layouts, close preview and preserve archives, deployed and unmanaged files");
    }
}
