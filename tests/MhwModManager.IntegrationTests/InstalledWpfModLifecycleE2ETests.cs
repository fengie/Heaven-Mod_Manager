using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Automation;
using MhwModManager.App;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

/// <summary>
/// Runs an actual published WinExe in a disposable Windows runner and drives its
/// native accessibility tree. The normal test suite reports SKIP; only the hosted
/// acceptance workflow opts in. Never point this at the user's live installation.
/// </summary>
[Collection(UpdaterGlobalStateFixture.Name)]
public sealed partial class InstalledWpfModLifecycleE2ETests
{
    public static bool IsOptedIn =>
        string.Equals(Environment.GetEnvironmentVariable("MHW_RUN_INSTALLED_WPF_E2E"),
            "1", StringComparison.Ordinal);

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;
    private static readonly TimeSpan UiTimeout = TimeSpan.FromSeconds(75);

    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public async Task AutoPopulateAppliesAnUnindexedLibraryForARegisteredUnrealGame()
    {
        using var run = new DisposableWpfRun(seedInbox: false, unreal: true);
        var expected = new Dictionary<string, byte[]>();
        for (var index = 0; index < 104; index++)
        {
            var folder = Directory.CreateDirectory(Path.Combine(run.InstalledRoot, $"Outfit {index:D3}"));
            var filename = $"outfit_{index:D3}_P.pak";
            byte[] payload = [31, (byte)index, 90];
            File.WriteAllBytes(Path.Combine(folder.FullName, filename), payload);
            expected.Add(Path.Combine(run.FirstGame.LiveModRoot, "~mods", filename), payload);
        }

        var window = await WaitForMainWindowAsync(run.Launch(), TestToken);
        await SelectModsTabAsync(window, TestToken);
        var button = await WaitForElementAsync(() => FindButton(window, "Auto Populate", enabled: true),
            UiTimeout, "Auto Populate for the registered game", TestToken);
        Invoke(button);
        await WaitForAsync(() => expected.Keys.All(File.Exists), TimeSpan.FromMinutes(3),
            "all 104 independent packages in the registered game's loader directory", TestToken);
        foreach (var (path, payload) in expected)
            Assert.Equal(payload, File.ReadAllBytes(path));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
    }

    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public async Task PreviouslyCatalogedCollectionShowsItsIndividualModsOnOpen()
    {
        using var run = new DisposableWpfRun(seedInbox: false, unreal: true);
        var wrapper = Directory.CreateDirectory(Path.Combine(run.InstalledRoot, "Travel shelf")).FullName;
        var first = Directory.CreateDirectory(Path.Combine(wrapper, "Blue Outfit")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(wrapper, "Deeper", "Gold Outfit")).FullName;
        var files = new Dictionary<string, byte[]>
        {
            [Path.Combine(first, "blue_P.pak")] = [1, 2, 3],
            [Path.Combine(first, "blue_P.sig")] = [9],
            [Path.Combine(second, "gold_P.pak")] = [4, 5, 6],
            [Path.Combine(second, "gold_P.sig")] = [9]
        };
        foreach (var choice in new[] { "Low", "High" })
        {
            var option = Directory.CreateDirectory(Path.Combine(wrapper, "Choose options", choice));
            files[Path.Combine(option.FullName, "option.pak")] = System.Text.Encoding.UTF8.GetBytes(choice);
        }
        foreach (var (path, bytes) in files) File.WriteAllBytes(path, bytes);
        var db = run.OpenCatalog();
        try
        {
            await db.InitializeAsync(TestToken);
            await db.InsertDiscoveredModAsync("old-shelf", "Travel shelf", "Travel shelf", wrapper, 10, TestToken);
            await db.InsertDiscoveredModAsync("saved-blue", "Blue Outfit", "Blue Outfit", first, 42, TestToken);

            var window = await WaitForMainWindowAsync(run.Launch(), TestToken);
            await SelectModsTabAsync(window, TestToken);
            var grid = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.DataGrid
                && e.Current.Name == "Mod library"), UiTimeout, "ordinary collection library", TestToken);
            foreach (var name in new[] { "Blue Outfit", "Gold Outfit" })
            {
                var row = await WaitForElementAsync(() => FindModToggle(grid, name), UiTimeout, name, TestToken);
                Assert.Equal(ToggleState.Off, ((TogglePattern)row.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
            }
            Assert.Null(FindModToggle(grid, "Travel shelf"));
            var mods = await db.GetModsAsync(TestToken);
            var retained = Assert.Single(mods, m => PathRules.Comparer.Equals(m.SourcePath, first));
            Assert.Equal("saved-blue", retained.Id);
            Assert.Equal(42, retained.Priority);
            Assert.Contains(mods, mod => mod.DisplayName == "Choose options" && mod.RequiresLayoutReview);
            var (dialog, invocation) = await OpenWorkflowAsync(window, TestToken);
            ((WindowPattern)dialog.GetCurrentPattern(WindowPattern.Pattern)).Close();
            await invocation.WaitAsync(UiTimeout, TestToken);
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(new[] { run.SentinelPath }, Directory.GetFiles(run.FirstGame.LiveModRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            await using var connection = await db.OpenAsync(CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
    }

    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public async Task VerifiedWorkshopItemAppearsInOrdinaryLibraryWithoutDeploymentControls()
    {
        using var run = new DisposableWpfRun(seedInbox: false, workshop: true);
        var window = await WaitForMainWindowAsync(run.Launch(), TestToken);
        await SelectModsTabAsync(window, TestToken);
        var grid = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.DataGrid && element.Current.Name == "Mod library"),
            UiTimeout, "ordinary mod library", TestToken);
        const string title = "新皮肤 Workshop fixture";
        var item = await WaitForElementAsync(() => Find(grid, element =>
            element.Current.Name == title), UiTimeout, "verified Workshop title in unfiltered Mods", TestToken);
        Assert.Null(Find(item, element => element.Current.ControlType == ControlType.CheckBox));
        var plugin = await WaitForElementAsync(() => Find(grid, element => element.Current.Name == "Example plugin"),
            UiTimeout, "externally owned BepInEx plugin", TestToken);
        Assert.Null(Find(plugin, element => element.Current.ControlType == ControlType.CheckBox));
        Assert.Null(Find(grid, element => element.Current.Name == "Workshop item 271828"));
        var selectAll = await WaitForElementAsync(() => FindButton(window, "Select all visible mods"),
            UiTimeout, "managed bulk selection", TestToken);
        Assert.False(selectAll.Current.IsEnabled);
        var search = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.Edit && element.Current.Name == "Search mods"),
            UiTimeout, "Search mods field", TestToken);
        var field = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern);
        foreach (var query in new[] { "新皮肤", "314159" })
        {
            field.SetValue("absent-workshop-fixture");
            await WaitForAsync(() => Find(grid, element => element.Current.Name == title) is null,
                UiTimeout, "Workshop row filtered out", TestToken);
            field.SetValue(query);
            await WaitForElementAsync(() => Find(grid, element => element.Current.Name == title),
                UiTimeout, "Workshop title restored by " + query, TestToken);
        }
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.WorkshopPayload));
        Assert.False(File.Exists(run.LiveModPath));
    }

    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public async Task InboxDropsAppearInOpenModsAndPreserveStagedSelections()
    {
        using var run = new DisposableWpfRun(seedInbox: false);
        var window = await WaitForMainWindowAsync(run.Launch(), TestToken);
        await SelectModsTabAsync(window, TestToken);
        var grid = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.DataGrid && element.Current.Name == "Mod library"),
            UiTimeout, "open Mods before dropping any packages", TestToken);

        Task<AutomationElement> WaitForToggle(string name) => WaitForElementAsync(() => Find(grid, element =>
            element.Current.ControlType == ControlType.CheckBox &&
            element.Current.Name.Contains(name, StringComparison.OrdinalIgnoreCase)),
            UiTimeout, "automatic Inbox row " + name, TestToken);
        var firstHash = SHA256.HashData(File.ReadAllBytes(run.BatchFirstArchive));
        var secondHash = SHA256.HashData(File.ReadAllBytes(run.BatchSecondArchive));
        run.CopyToInbox(run.BatchFirstArchive);
        var first = (TogglePattern)(await WaitForToggle("BulkOne")).GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, first.Current.ToggleState);
        first.Toggle();
        run.CopyToInbox(run.BatchSecondArchive);
        var second = (TogglePattern)(await WaitForToggle("BulkTwo")).GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, second.Current.ToggleState);
        first = (TogglePattern)(await WaitForToggle("BulkOne")).GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.On, first.Current.ToggleState);
        Assert.Equal(firstHash, SHA256.HashData(File.ReadAllBytes(run.BatchFirstArchive)));
        Assert.Equal(secondHash, SHA256.HashData(File.ReadAllBytes(run.BatchSecondArchive)));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        Assert.False(File.Exists(Path.Combine(run.FirstGame.LiveModRoot, "bulk-one.bin")));
        Assert.False(File.Exists(Path.Combine(run.FirstGame.LiveModRoot, "bulk-two.bin")));
    }

    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public async Task InboxImportPreviewApplyUndoAndGameSwitchPreserveExactFiles()
    {
        using var run = new DisposableWpfRun(seedInbox: true);
        var app = run.Launch();
        var window = await WaitForMainWindowAsync(app, TestToken);
        // The top-level native window can exist before its WPF accessibility tree
        // is populated; wait for the real selector instead of racing startup.
        await WaitForElementAsync(() => Find(window, element =>
            element.Current.AutomationId == "ActiveGameSelector"),
            UiTimeout, "initial active game selector", TestToken);
        Assert.Equal(run.FirstGame.DisplayName, ReadActiveGame(window));

        var moreTools = await WaitForElementAsync(() => Find(window, element =>
            element.Current.Name == "More tools"), UiTimeout, "Dashboard More tools", TestToken);
        if (!moreTools.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand) ||
            expand is not ExpandCollapsePattern panel)
            throw new InvalidOperationException("More tools does not expose accessible ExpandCollapse.");
        panel.Expand();

        var inboxButton = await WaitForElementAsync(() => FindButton(window, "Install Mods from Inbox"),
            UiTimeout, "Install Mods from Inbox button", TestToken);
        Invoke(inboxButton);
        await WaitForAsync(() => Directory.Exists(run.InstalledModPath) &&
            File.Exists(Path.Combine(run.InstalledModPath, "e2e-payload.bin")),
            UiTimeout, "Inbox never published the approved fixture", TestToken);

        await SelectModsTabAsync(window, TestToken);
        var toggle = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.CheckBox &&
            element.Current.Name.Contains("E2E Inbox Fixture", StringComparison.OrdinalIgnoreCase)),
            UiTimeout, "Indexed Inbox mod checkbox", TestToken);
        var state = (TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, state.Current.ToggleState);
        Assert.False(File.Exists(run.LiveModPath));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));

        state.Toggle();
        Assert.Equal(ToggleState.On,
            ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);

        Invoke(await WaitForElementAsync(() => FindButton(window, "Preview Mod Changes", enabled: true),
            UiTimeout, "Preview Mod Changes", TestToken));
        await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.Text &&
            element.Current.Name.Contains("Dry run passed", StringComparison.OrdinalIgnoreCase)),
            UiTimeout, "successful dry-run result", TestToken);
        Assert.False(File.Exists(run.LiveModPath));

        Invoke(await WaitForElementAsync(() => FindButton(window, "Apply Changes", enabled: true),
            UiTimeout, "Apply Changes", TestToken));
        await WaitForAsync(() => File.Exists(run.LiveModPath) &&
            File.ReadAllBytes(run.LiveModPath).SequenceEqual(run.ModBytes),
            UiTimeout, "Apply did not write the exact expected fixture bytes", TestToken);
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));

        await SelectModsTabAsync(window, TestToken);
        Invoke(await WaitForElementAsync(() => FindButton(window, "Undo Last Applied Change", enabled: true),
            UiTimeout, "Undo Last Applied Change", TestToken));
        await WaitForAsync(() => !File.Exists(run.LiveModPath),
            UiTimeout, "Undo left the installed fixture in the fake game", TestToken);
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));

        // The switch must reuse the exact same WPF PID and keep game A's files intact.
        var selector = await WaitForElementAsync(() => Find(window, element =>
            element.Current.AutomationId == "ActiveGameSelector"),
            UiTimeout, "active game selector", TestToken);
        var expander = (ExpandCollapsePattern)selector.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        expander.Expand();
        var nextGame = await WaitForElementAsync(() =>
            Find(selector, element => element.Current.ControlType == ControlType.ListItem &&
                element.Current.Name.Contains(run.SecondGame.DisplayName, StringComparison.Ordinal)) ??
            Find(window, element => element.Current.ControlType == ControlType.ListItem &&
                element.Current.Name.Contains(run.SecondGame.DisplayName, StringComparison.Ordinal)),
            UiTimeout, "second registered game", TestToken);
        var selection = (SelectionItemPattern)nextGame.GetCurrentPattern(SelectionItemPattern.Pattern);
        selection.Select();
        Invoke(await WaitForElementAsync(() => FindButton(window, "Switch", enabled: true),
            UiTimeout, "Switch game", TestToken));
        await WaitForAsync(() => ReadActiveGame(window) == run.SecondGame.DisplayName,
            UiTimeout, "game selector never switched to the second fake profile", TestToken);
        Assert.False(app.HasExited);
        Assert.Equal(app.Id, window.Current.ProcessId);
        Assert.False(File.Exists(run.LiveModPath));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
    }

    [Theory(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeArchivePickerImportsOneDisabledModWithoutTouchingFakeGame(bool unreal)
    {
        using var run = new DisposableWpfRun(seedInbox: false, unreal: unreal);
        var app = run.Launch();
        var window = await WaitForMainWindowAsync(app, TestToken);
        var originalHash = SHA256.HashData(File.ReadAllBytes(run.ArchivePath));

        // Explicitly present the Mods view before locating its native import
        // control; WPF tabs can defer accessibility children until selected.
        await SelectModsTabAsync(window, TestToken);
        Invoke(await WaitForElementAsync(() => FindButton(window, "Install Mod"),
            UiTimeout, "Install Mod", TestToken));
        var dialog = await WaitForElementAsync(() => FindFileDialog(app.Id),
            UiTimeout, "native archive file picker", TestToken);
        var fileNameEdit = Find(dialog, element =>
            element.Current.ControlType == ControlType.Edit &&
            (element.Current.AutomationId == "1148" ||
             element.Current.Name.Contains("File name", StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException("Windows file picker has no accessible file-name field.");
        if (!fileNameEdit.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) ||
            pattern is not ValuePattern field)
            throw new InvalidOperationException("Windows file picker has no editable ValuePattern.");
        field.SetValue(run.ArchivePath);
        Invoke(await WaitForElementAsync(() => FindButton(dialog, "Open", enabled: true),
            UiTimeout, "native file picker Open", TestToken));

        await WaitForAsync(() => Directory.Exists(run.InstalledModPath) &&
            File.Exists(Path.Combine(run.InstalledModPath, unreal ? "Quartz/Content/Paks/~mods/e2e.pak" : "e2e-payload.bin")),
            UiTimeout, "file picker import never reached installed library", TestToken);
        await SelectModsTabAsync(window, TestToken);
        var toggle = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.CheckBox &&
            element.Current.Name.Contains("E2E Inbox Fixture", StringComparison.OrdinalIgnoreCase)),
            UiTimeout, "newly imported mod in visible WPF list", TestToken);
        Assert.Equal(ToggleState.Off,
            ((TogglePattern)toggle.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        Assert.False(File.Exists(run.LiveModPath));
        Assert.Equal(run.ModBytes, File.ReadAllBytes(Path.Combine(run.InstalledModPath,
            unreal ? "Quartz/Content/Paks/~mods/e2e.pak" : "e2e-payload.bin")));
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(run.ArchivePath)));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
    }


    [Fact(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [Trait("Category", "InstalledWpf")]
    public async Task BulkArchivePickerImmediatelyIndexesSafeModsForActiveGame()
    {
        using var run = new DisposableWpfRun(seedInbox: false);
        var app = run.Launch();
        var window = await WaitForMainWindowAsync(app, TestToken);
        var originalA = SHA256.HashData(File.ReadAllBytes(run.BatchFirstArchive));
        var originalB = SHA256.HashData(File.ReadAllBytes(run.BatchSecondArchive));

        await SelectModsTabAsync(window, TestToken);
        Invoke(await WaitForElementAsync(() => FindButton(window, "Install Mod"),
            UiTimeout, "bulk Install Mod", TestToken));
        var dialog = await WaitForElementAsync(() => FindFileDialog(app.Id),
            UiTimeout, "native multi-archive file picker", TestToken);
        var name = Find(dialog, element => element.Current.ControlType == ControlType.Edit &&
            (element.Current.AutomationId == "1148" ||
             element.Current.Name.Contains("File name", StringComparison.OrdinalIgnoreCase)))
            ?? throw new InvalidOperationException("Native picker has no editable filename field.");
        var fileName = (ValuePattern)name.GetCurrentPattern(ValuePattern.Pattern);
        fileName.SetValue($"\"{run.BatchFirstArchive}\" \"{run.BatchSecondArchive}\"");
        Invoke(await WaitForElementAsync(() => FindButton(dialog, "Open", enabled: true),
            UiTimeout, "native multi-archive Open", TestToken));

        // These checkboxes are created only when the real Inbox → catalog workflow
        // has completed. Previously both ZIPs remained in Inbox with no library rows.
        await SelectModsTabAsync(window, TestToken);
        var a = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.CheckBox &&
            element.Current.Name.Contains("e2e-game-a-BulkOne", StringComparison.OrdinalIgnoreCase)),
            UiTimeout, "first bulk-imported mod", TestToken);
        var b = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.CheckBox &&
            element.Current.Name.Contains("e2e-game-a-BulkTwo", StringComparison.OrdinalIgnoreCase)),
            UiTimeout, "second bulk-imported mod", TestToken);
        Assert.Equal(ToggleState.Off, ((TogglePattern)a.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        Assert.Equal(ToggleState.Off, ((TogglePattern)b.GetCurrentPattern(TogglePattern.Pattern)).Current.ToggleState);
        Assert.Equal(originalA, SHA256.HashData(File.ReadAllBytes(run.BatchFirstArchive)));
        Assert.Equal(originalB, SHA256.HashData(File.ReadAllBytes(run.BatchSecondArchive)));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        Assert.False(File.Exists(Path.Combine(run.FirstGame.LiveModRoot, "bulk-one.bin")));
        Assert.False(File.Exists(Path.Combine(run.FirstGame.LiveModRoot, "bulk-two.bin")));
    }

    [Theory(Skip = "Requires the disposable hosted Windows acceptance workflow", SkipUnless = nameof(IsOptedIn))]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [Trait("Category", "InstalledWpf")]
    public async Task NestedUnrealPackagesRemainSeparateThroughPreviewApplyAndUndo(bool archive, bool alreadyManaged)
    {
        using var run = new DisposableWpfRun(seedInbox: false, unreal: true);
        var input = Path.Combine(Path.GetDirectoryName(alreadyManaged ? run.InstalledModPath : run.ArchivePath)!, "Nested Unreal Collection");
        var members = new[] { "first.pak", "first.utoc", "first.ucas" };
        var shelf = alreadyManaged ? input : Path.Combine(input, "Mods", "Shelf");
        var firstRoot = Directory.CreateDirectory(Path.Combine(shelf, "NestedFirst",
            alreadyManaged ? "" : run.FirstGame.ModRootRelativePath, "~mods")).FullName;
        var secondRoot = Directory.CreateDirectory(Path.Combine(shelf, "NestedSecond",
            alreadyManaged ? "" : run.FirstGame.ModRootRelativePath, "~mods")).FullName;
        var sources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < members.Length; index++)
            sources.Add(Path.Combine(firstRoot, members[index]), [(byte)(index + 1), 19, 37]);
        sources.Add(Path.Combine(secondRoot, "second.pak"), [42, 17, 78]);
        foreach (var source in sources)
            File.WriteAllBytes(source.Key, source.Value);

        var selectedInput = archive ? input + ".zip" : input;
        if (archive) ZipFile.CreateFromDirectory(input, selectedInput);
        var archiveHash = archive ? SHA256.HashData(File.ReadAllBytes(selectedInput)) : null;
        var gameRoot = run.FirstGame.GameRoot;
        var before = Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var expected = members.Select(name => Path.Combine(run.FirstGame.LiveModRoot, "~mods", name)).ToArray();

        var app = run.Launch();
        var window = await WaitForMainWindowAsync(app, TestToken);
        if (archive) await SelectModsTabAsync(window, TestToken);
        if (!alreadyManaged)
        {
            var importName = archive ? "Install Mod" : "Import ZIP/RAR/7z Folder";
            Invoke(await WaitForElementAsync(() => FindButton(window, importName, enabled: true),
                UiTimeout, importName, TestToken));
            var dialog = await WaitForElementAsync(() => FindFileDialog(app.Id, folder: !archive),
                UiTimeout, "nested collection picker", TestToken);
            var edit = FindPickerField(dialog, folder: !archive)
                ?? throw new InvalidOperationException("Nested collection picker has no accessible path field.");
            ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(selectedInput);
            var confirm = archive ? "Open" : "Select Folder";
            Invoke(await WaitForElementAsync(() => FindButton(dialog, confirm, enabled: true),
                UiTimeout, "confirm nested input", TestToken));
        }

        await SelectModsTabAsync(window, TestToken);
        var first = (TogglePattern)(await WaitForElementAsync(() => FindModToggle(window, "NestedFirst"),
            UiTimeout, "first routed Unreal package", TestToken)).GetCurrentPattern(TogglePattern.Pattern);
        var second = (TogglePattern)(await WaitForElementAsync(() => FindModToggle(window, "NestedSecond"),
            UiTimeout, "second routed Unreal package", TestToken)).GetCurrentPattern(TogglePattern.Pattern);
        Assert.Equal(ToggleState.Off, first.Current.ToggleState);
        Assert.Equal(ToggleState.Off, second.Current.ToggleState);
        Assert.Equal(before, Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        first.Toggle();

        Invoke(await WaitForElementAsync(() => FindButton(window, "Preview Mod Changes", enabled: true),
            UiTimeout, "preview the first nested package", TestToken));
        await WaitForElementAsync(() => Find(window, element => element.Current.ControlType == ControlType.Text &&
            element.Current.Name.Contains("Dry run passed. Ready", StringComparison.Ordinal) &&
            element.Current.Name.Contains("3 add", StringComparison.Ordinal) &&
            element.Current.Name.Contains("0 replace", StringComparison.Ordinal) &&
            element.Current.Name.Contains("0 remove", StringComparison.Ordinal) &&
            element.Current.Name.Contains("0 restore", StringComparison.Ordinal)),
            UiTimeout, "preview exactly three new companion files", TestToken);
        Assert.Equal(before, Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));

        Invoke(await WaitForElementAsync(() => FindButton(window, "Apply Changes", enabled: true),
            UiTimeout, "apply the first nested package", TestToken));
        await WaitForAsync(() => expected.All(File.Exists), UiTimeout,
            "complete companion set in the exact fake loader destination", TestToken);
        Assert.Equal(before.Concat(expected).Order(StringComparer.Ordinal),
            Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(sources[Path.Combine(firstRoot, members[index])], File.ReadAllBytes(expected[index]));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));

        await SelectModsTabAsync(window, TestToken);
        Invoke(await WaitForElementAsync(() => FindButton(window, "Undo Last Applied Change", enabled: true),
            UiTimeout, "undo first nested package", TestToken));
        await WaitForAsync(() => expected.All(path => !File.Exists(path)), UiTimeout,
            "undo removed the exact companion set", TestToken);
        Assert.Equal(before, Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        foreach (var source in sources)
            Assert.Equal(source.Value, File.ReadAllBytes(source.Key));
        if (archiveHash is not null)
            Assert.Equal(archiveHash, SHA256.HashData(File.ReadAllBytes(selectedInput)));
    }

    [Fact]
    public void PreflightRejectsNonDisposableOrUnapprovedRunner()
    {
        Assert.Throws<InvalidOperationException>(() => DisposableWpfRun.GuardRunner(
            "1", "self-hosted", "Windows", @"C:\test", @"C:\test\build"));
        Assert.Throws<InvalidOperationException>(() => DisposableWpfRun.GuardRunner(
            "0", "github-hosted", "Windows", @"C:\test", @"C:\test\build"));
        Assert.Throws<InvalidOperationException>(() => DisposableWpfRun.GuardRunner(
            "1", "github-hosted", "Linux", @"C:\test", @"C:\test\build"));
        Assert.Throws<InvalidOperationException>(() => DisposableWpfRun.GuardRunner(
            "1", "github-hosted", "Windows", @"C:\test", @"D:\foreign"));
    }

    private static string ReadActiveGame(AutomationElement window)
    {
        var selector = Find(window, element =>
            element.Current.AutomationId == "ActiveGameSelector")
            ?? throw new InvalidOperationException("Installed active game selector disappeared.");
        return selector.Current.ItemStatus?.Trim() ?? string.Empty;
    }

    private static async Task SelectModsTabAsync(AutomationElement window, CancellationToken ct)
    {
        var tab = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.TabItem &&
            element.Current.Name == "Mods"), UiTimeout, "Mods tab", ct);
        var selected = (SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern);
        selected.Select();
    }

    private static async Task<AutomationElement> WaitForMainWindowAsync(Process process, CancellationToken ct) =>
        await WaitForElementAsync(() =>
        {
            if (process.HasExited)
                throw new InvalidOperationException("Packaged WPF process terminated before showing the main window.");
            var roots = AutomationElement.RootElement.FindAll(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
            foreach (AutomationElement window in roots)
            {
                if (window.Current.ControlType != ControlType.Window) continue;
                var title = window.Current.Name;
                // Splash uses "Universal Mod Manager" and a startup-failure modal
                // uses "Universal Mod Manager startup failed". Neither has the real
                // UI controls. Never mistake either for the product's main window.
                if (title is "Universal Mod Manager startup failed" or "Universal Mod Manager unexpected UI failure")
                {
                    var message = Find(window, element =>
                        element.Current.ControlType == ControlType.Text &&
                        element.Current.Name.Contains("Exception", StringComparison.OrdinalIgnoreCase));
                    var detail = message?.Current.Name ?? "<no accessible error detail>";
                    throw new InvalidOperationException(
                        "Packaged mod manager failed during startup: " +
                        detail[..Math.Min(800, detail.Length)]);
                }
                if (title.StartsWith("Universal Mod Manager — ", StringComparison.Ordinal))
                    return window;
                if (window.FindFirst(TreeScope.Descendants, new PropertyCondition(
                    AutomationElement.AutomationIdProperty, "ActiveGameSelector")) is not null)
                    throw new InvalidOperationException(
                        $"Packaged main window has the game selector but an unexpected title: '{title}'.");
            }
            return null;
        }, UiTimeout, "packaged WPF main window", ct);

    private static AutomationElement? FindFileDialog(int pid, bool folder = false)
    {
        var windows = AutomationElement.RootElement.FindAll(TreeScope.Children,
            new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
        foreach (AutomationElement element in windows)
        {
            if (FindPickerField(element, folder) is not null) return element;
        }
        return null;
    }

    private static AutomationElement? FindPickerField(AutomationElement dialog, bool folder) =>
        Find(dialog, node => node.Current.ControlType == ControlType.Edit &&
            (node.Current.AutomationId == "1148" ||
             node.Current.Name.Contains(folder ? "Folder:" : "File name", StringComparison.OrdinalIgnoreCase)));

    private static AutomationElement? FindModToggle(AutomationElement window, string name) =>
        Find(window, element => element.Current.ControlType == ControlType.CheckBox &&
            element.Current.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static AutomationElement? FindButton(AutomationElement root, string name, bool enabled = false) =>
        Find(root, element => element.Current.ControlType == ControlType.Button &&
            element.Current.Name == name && (!enabled || element.Current.IsEnabled));

    private static AutomationElement? Find(AutomationElement root, Func<AutomationElement, bool> predicate)
    {
        var walker = TreeWalker.RawViewWalker;
        var pending = new Stack<AutomationElement>();
        pending.Push(root);
        var visited = 0;
        while (pending.Count > 0 && visited++ < 2500)
        {
            var current = pending.Pop();
            try
            {
                if (predicate(current)) return current;
                var child = walker.GetFirstChild(current);
                var sibling = ReferenceEquals(current, root) ? null : walker.GetNextSibling(current);
                if (sibling is not null) pending.Push(sibling);
                if (child is not null) pending.Push(child);
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }
        return null;
    }

    private static void Invoke(AutomationElement button)
    {
        if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out var pattern) ||
            pattern is not InvokePattern invoke)
            throw new InvalidOperationException("Expected an accessible InvokePattern button: " +
                button.Current.Name);
        invoke.Invoke();
    }

    private static async Task<AutomationElement> WaitForElementAsync(
        Func<AutomationElement?> find, TimeSpan timeout, string operation, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = find();
                if (result is not null) return result;
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            await Task.Delay(200, ct);
        }
        throw new TimeoutException("Installed WPF accessibility timeout: " + operation);
    }

    private static async Task WaitForAsync(Func<bool> succeeded, TimeSpan timeout,
        string operation, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (succeeded()) return;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            await Task.Delay(200, ct);
        }
        throw new TimeoutException("Installed WPF functional timeout: " + operation);
    }

    private sealed class DisposableWpfRun : IDisposable
    {
        private readonly string root;
        private readonly string marker;
        private Process? app;
        private readonly string home;
        private readonly string gameA;
        private readonly string gameB;

        public GameProfile FirstGame { get; }
        public GameProfile SecondGame { get; }
        public string ArchivePath { get; }
        public string ManagerHome => home;
        private string ProfileState => Path.Combine(home, "State", FirstGame.IsMonsterHunterWorld ? "" : Path.Combine("Games", FirstGame.Id), "Next");
        public string SettingsPath => Path.Combine(ProfileState, "settings.json");
        public GameProfile RegisteredGame() => new GameProfileRegistry(Path.Combine(home, "State")).Load().Single(game => game.Id == FirstGame.Id);
        public string LaunchReceipt => Path.Combine(root, "game-launch.txt");
        public string InstalledRoot => GameModStorage.Resolve(home, FirstGame).InstalledRoot;
        public MhwModManager.Storage.ManagerDatabase OpenCatalog() =>
            new(Path.Combine(ProfileState, "manager.db"));
        public string BatchFirstArchive { get; }
        public string BatchSecondArchive { get; }
        public string InstalledModPath { get; }
        public string LiveModPath { get; }
        public string SentinelPath { get; }
        public byte[] ModBytes { get; } = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();
        public byte[] BaselineBytes { get; } = [13, 37, 73, 101];

        public string WorkshopPayload => Path.Combine(root, "steamapps", "workshop", "content", FirstGame.SteamAppId!, "314159", "payload.bin");

        public DisposableWpfRun(bool seedInbox, bool unreal = false, bool workshop = false, GameProfile? profile = null, bool moved = false)
        {
            var temp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            var packageDir = Environment.GetEnvironmentVariable("MHW_WPF_E2E_PACKAGE_DIR");
            GuardRunner(Environment.GetEnvironmentVariable("MHW_RUN_INSTALLED_WPF_E2E"),
                Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"),
                Environment.GetEnvironmentVariable("RUNNER_OS"), temp, packageDir);
            var fullTemp = Path.GetFullPath(temp!);
            root = Path.Combine(fullTemp, "mhw-wpf-acceptance-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            marker = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(root, ".owned-e2e-root"), marker);
            home = Path.Combine(root, "manager-home");
            gameA = workshop || !string.IsNullOrWhiteSpace(profile?.SteamAppId) ? Path.Combine(root, "steamapps", "common", "game-A") : Path.Combine(root, "game-A");
            gameB = Path.Combine(root, "game-B");
            Directory.CreateDirectory(gameA);
            Directory.CreateDirectory(gameB);
            Directory.CreateDirectory(Path.Combine(gameA, "Mods"));
            Directory.CreateDirectory(Path.Combine(gameB, "Mods"));
            var cmd = Environment.GetEnvironmentVariable("ComSpec") ?? Path.Combine(
                Environment.SystemDirectory, "cmd.exe");
            File.Copy(cmd, Path.Combine(gameA, "FakeA.exe"));
            File.Copy(cmd, Path.Combine(gameB, "FakeB.exe"));

            FirstGame = GameProfile.Generic("e2e-game-a", "E2E Fake Game A", gameA, "FakeA.exe", "Mods");
            if (workshop) FirstGame = FirstGame with { SteamAppId = "987654321" };
            if (unreal)
                FirstGame = FirstGame with { AdapterId = "unreal-paks", ModRootRelativePath = @"Quartz\Content\Paks" };
            if (profile is not null)
            {
                FirstGame = profile with
                {
                    GameRoot = gameA,
                    ExecutableRelativePath = GameProfile.NormalizeRelative(profile.ExecutableRelativePath, false),
                    ModRootRelativePath = GameProfile.NormalizeRelative(profile.ModRootRelativePath, true),
                    SavePath = profile.SavePath is null ? null : Path.Combine(root, "saves")
                };
                Directory.CreateDirectory(Path.GetDirectoryName(FirstGame.ExecutablePath)!);
                File.Copy(Path.Combine(fullTemp, "mhw-game-launch-probe.exe"), FirstGame.ExecutablePath, overwrite: true);
                if (FirstGame.SavePath is not null) Directory.CreateDirectory(FirstGame.SavePath);
            }
            if (FirstGame.SteamAppId is { } steamId)
            {
                if (!uint.TryParse(steamId, out _)) throw new InvalidDataException("Invalid Steam app ID.");
                var workshopRoot = Path.Combine(root, "steamapps", "workshop");
                Directory.CreateDirectory(Path.GetDirectoryName(WorkshopPayload)!);
                Directory.CreateDirectory(Path.Combine(workshopRoot, "content", steamId, "271828"));
                File.WriteAllBytes(WorkshopPayload, ModBytes);
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(WorkshopPayload)!, "data.json"),
                    """{"folderName":"314159","id":314159,"title":"新皮肤 Workshop fixture"}""");
                File.WriteAllText(Path.Combine(workshopRoot, $"appworkshop_{steamId}.acf"),
                    $$""" "AppWorkshop" { "appid" "{{steamId}}" "WorkshopItemsInstalled" { "314159" { "manifest" "1" } } } """);
            }
            if (workshop || FirstGame.AdapterId == "bepinex")
            {
                var pluginDir = Directory.CreateDirectory(Path.Combine(gameA,"BepInEx","plugins"));
                BepInExPluginLibraryTests.WritePlugin(Path.Combine(pluginDir.FullName,"sample.dll"));
            }
            SecondGame = GameProfile.Generic("e2e-game-b", "E2E Fake Game B", gameB, "FakeB.exe", "Mods");
            var state = Path.Combine(home, "State");
            var registry = new GameProfileRegistry(state);
            registry.Upsert(!moved ? FirstGame : FirstGame with { GameRoot = Path.Combine(root, "previous-game-location") });
            registry.Upsert(SecondGame);
            registry.SetActive(FirstGame.Id);

            foreach (var game in new[] { FirstGame, SecondGame })
            {
                var settingPath = game.IsMonsterHunterWorld ? Path.Combine(state, "Next", "settings.json") :
                    Path.Combine(state, "Games", game.Id, "Next", "settings.json");
                ManagerSettingsStore.Load(settingPath).Update(settings =>
                {
                    settings.AutoUpdateEnabled = false;
                    settings.BackgroundMetadataRefreshEnabled = false;
                    settings.ConfirmBeforeApply = false;
                    settings.UiAnimationsEnabled = false;
                });
            }

            var input = Path.Combine(root, "input");
            Directory.CreateDirectory(input);
            ArchivePath = Path.Combine(input, "E2E Inbox Fixture.zip");
            var pakProfile = FirstGame.AdapterId == "unreal-paks";
            var inMount = pakProfile && Path.GetFileName(FirstGame.ModRootRelativePath).Equals("Paks", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine("~mods", "e2e.pak") : pakProfile ? "e2e.pak" : "e2e-payload.bin";
            var archiveMember = Path.Combine(FirstGame.ModRootRelativePath, inMount).Replace('\\', '/');
            using (var zip = ZipFile.Open(ArchivePath, ZipArchiveMode.Create))
            {
                using var payload = zip.CreateEntry(archiveMember,
                    CompressionLevel.NoCompression).Open();
                payload.Write(ModBytes);
            }

            BatchFirstArchive = Path.Combine(input, "e2e-game-a-BulkOne.zip");
            BatchSecondArchive = Path.Combine(input, "e2e-game-a-BulkTwo.zip");
            using (var first = ZipFile.Open(BatchFirstArchive, ZipArchiveMode.Create))
            {
                using var payload = first.CreateEntry("Mods/bulk-one.bin", CompressionLevel.NoCompression).Open();
                payload.Write(ModBytes);
            }
            using (var second = ZipFile.Open(BatchSecondArchive, ZipArchiveMode.Create))
            {
                using var payload = second.CreateEntry("Mods/bulk-two.bin", CompressionLevel.NoCompression).Open();
                payload.Write(ModBytes);
            }

            var storage = GameModStorage.Resolve(home, FirstGame);
            InstalledModPath = Path.Combine(storage.InstalledRoot, "E2E Inbox Fixture");
            LiveModPath = Path.Combine(FirstGame.LiveModRoot, inMount);
            Directory.CreateDirectory(FirstGame.LiveModRoot);
            SentinelPath = Path.Combine(FirstGame.LiveModRoot, "keep-user-owned.bin");
            File.WriteAllBytes(SentinelPath, BaselineBytes);
            if (seedInbox) CopyToInbox(ArchivePath);
        }

        public void CopyToInbox(string source)
        {
            var inbox = GameModStorage.Resolve(home, FirstGame).InboxRoot;
            Directory.CreateDirectory(inbox);
            File.Copy(source, Path.Combine(inbox, Path.GetFileName(source)));
        }

        public static void GuardRunner(string? optIn, string? runnerEnvironment,
            string? runnerOs, string? runnerTemp, string? packageDir)
        {
            if (optIn != "1" || runnerEnvironment != "github-hosted" || runnerOs != "Windows")
                throw new InvalidOperationException(
                    "Installed WPF UI automation requires explicit opt-in and a disposable GitHub-hosted Windows VM.");
            if (string.IsNullOrWhiteSpace(runnerTemp) || string.IsNullOrWhiteSpace(packageDir))
                throw new InvalidOperationException("Disposable runner paths are not configured.");
            var temp = Path.GetFullPath(runnerTemp).TrimEnd(Path.DirectorySeparatorChar);
            var package = Path.GetFullPath(packageDir);
            if (!package.StartsWith(temp + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Packaged candidate is not under the disposable runner root.");
        }

        public Process Launch()
        {
            var packageDir = Path.GetFullPath(
                Environment.GetEnvironmentVariable("MHW_WPF_E2E_PACKAGE_DIR")!);
            var executable = Path.Combine(packageDir, "MHW Mod Manager.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Candidate WinExe was not published in the disposable workspace.", executable);
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = packageDir,
                UseShellExecute = false
            };
            start.ArgumentList.Add("--offline-catalog");
            start.Environment["MHW_DISPOSABLE_GAME_RECEIPT"] = LaunchReceipt;
            start.Environment["MOD_MANAGER_HOME"] = home;
            start.Environment["MOD_MANAGER_GAME_ROOT"] = gameA;
            start.Environment["MOD_MANAGER_DEBUG_ROOT"] = Path.Combine(root, "diagnostics");
            start.Environment["MHWMM_DIAGNOSTIC"] = "0";
            app = Process.Start(start) ??
                throw new InvalidOperationException("Could not launch the isolated packaged WPF application.");
            return app;
        }

        public void Dispose()
        {
            try
            {
                if (app is { HasExited: false })
                {
                    if (app.CloseMainWindow() && app.WaitForExit(4000)) { }
                    else
                    {
                        app.Kill(entireProcessTree: true);
                        app.WaitForExit(5000);
                    }
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { app?.Dispose(); }

            // Never delete unmarked, foreign, or reparse-point roots.
            var temp = Environment.GetEnvironmentVariable("RUNNER_TEMP");
            if (string.IsNullOrWhiteSpace(temp) || !Directory.Exists(root)) return;
            var fullTemp = Path.GetFullPath(temp).TrimEnd(Path.DirectorySeparatorChar);
            if (!root.StartsWith(fullTemp + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(root).StartsWith("mhw-wpf-acceptance-", StringComparison.Ordinal) ||
                (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
                File.ReadAllText(Path.Combine(root, ".owned-e2e-root")) != marker)
                throw new InvalidOperationException("Refusing to clean an unowned WPF acceptance root.");
            Directory.Delete(root, recursive: true);
        }
    }
}
