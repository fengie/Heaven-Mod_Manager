using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    [DllImport("user32.dll", EntryPoint="PostMessageW", ExactSpelling=true, SetLastError=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int PostCloseMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetWindowThreadProcessId(nint window,out int processId);

    private const string ChoiceTitle = "Traveller Outfit";
    private const string AddonTitle = "Expedition Kit";
    private static readonly string[] ChoiceLabels = ["NO_Cape", "WITH_Cape"];
    private static readonly string[] ChoiceFiles = ["traveller.pak", "traveller.utoc", "traveller.ucas"];
    private static readonly string[] AddonLabels = ["Main", "Compass", "Lantern"];

    private static async Task<Dictionary<string, byte[]>> SeedPackagedFamiliesAsync(DisposableWpfRun run, CancellationToken ct)
    {
        var original = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            original.Add(path, Encoding.UTF8.GetBytes(text));
            File.WriteAllBytes(path, original[path]);
        }

        var choice = Path.Combine(run.InstalledRoot, ChoiceTitle);
        Write(Path.Combine(choice, "README.txt"), "Choose one option: NO_Cape or WITH_Cape. Install only one.");
        foreach (var label in ChoiceLabels)
            foreach (var file in ChoiceFiles)
                Write(Path.Combine(choice, label, file), label + " " + file);

        // An existing, explicitly linked family is an additive control for the authored one-of family.
        var db = run.OpenCatalog();
        await db.InitializeAsync(ct);
        foreach (var label in AddonLabels)
        {
            var name = label == "Main" ? AddonTitle : AddonTitle + " - " + label;
            var source = Path.Combine(run.InstalledRoot, name);
            Write(Path.Combine(source, label + ".pak"), "kit " + label);
            await db.UpsertModAsync(new ModDescriptor(label, name, name, source, false, 20), ct);
        }
        await db.ChainManualFamilyAsync("Main", ["Main"], [["Compass"], ["Lantern"]], AddonTitle, ct);
        await using var connection = await db.OpenAsync(ct);
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        return original;
    }

    private static async Task ExercisePackagedFamiliesAsync(DisposableWpfRun run, Process app, AutomationElement window,
        Dictionary<string, byte[]> original, CancellationToken ct)
    {
        var gameRoot = run.FirstGame.GameRoot;
        var mount = Path.Combine(run.FirstGame.LiveModRoot, "~mods");
        var baseline = Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        async Task OpenFamily(string name)
        {
            await SelectModsTabAsync(window, ct);
            // File publication can precede completion of the transaction and
            // WPF row refresh. Do not drive an input/selection through the
            // operation overlay: wait until the app is interactable, then
            // exercise the real auto-filter without an Enter-key workaround.
            await WaitForAsync(() => window.Current.IsEnabled &&
                Find(window, e => e.Current.Name == "Current operation phase" &&
                    !e.Current.IsOffscreen) is null,
                UiTimeout, "WPF ready before opening family " + name, ct);
            var search = await WaitForElementAsync(() => Find(window, e =>
                e.Current.ControlType == ControlType.Edit && e.Current.Name == "Search mods"), UiTimeout, "Mods search", ct);
            ((ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern)).SetValue(name);
            await WaitForAsync(() => ((ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == name,
                UiTimeout, "WPF search accepted family " + name, ct);
            try
            {
                await WaitForElementAsync(() =>
                {
                    var row = Find(window, e => e.Current.ControlType == ControlType.CheckBox && e.Current.Name == name);
                    while (row is not null && row.Current.ControlType != ControlType.DataItem)
                        row = TreeWalker.RawViewWalker.GetParent(row);
                    if (row is null || !row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var pattern)) return null;
                    var selection = (SelectionItemPattern)pattern;
                    if (!selection.Current.IsSelected) selection.Select();
                    return selection.Current.IsSelected ? row : null;
                }, UiTimeout, "family " + name, ct);
            }
            catch (TimeoutException error)
            {
                string state;
                try
                {
                    var currentSearch = Find(window, e => e.Current.ControlType == ControlType.Edit && e.Current.Name == "Search mods");
                    var query = currentSearch?.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    var grid = Find(window, e => e.Current.ControlType == ControlType.DataGrid && e.Current.Name == "Mod library");
                    var dataRows = grid?.FindAll(TreeScope.Children,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem))
                        .Cast<AutomationElement>().Take(8).ToArray() ?? [];
                    var realized = dataRows.Length;
                    var realizedNames = dataRows.Select(row => row.Current.Name).ToArray();
                    var selectable = dataRows.Count(row =>
                        row.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _));
                    var checkboxes = grid?.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox))
                        .Cast<AutomationElement>().Take(12)
                        .Select(box => box.Current.Name + (box.Current.IsOffscreen ? " (offscreen)" : ""))
                        .ToArray() ?? [];
                    var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, app.Id))
                        .Cast<AutomationElement>().Take(6).Select(e => e.Current.ClassName + " " + e.Current.Name).ToArray();
                    var catalog = run.OpenCatalog();
                    await using var pool = await catalog.OpenAsync(ct);
                    try
                    {
                        var mods = await catalog.GetModsAsync(ct);
                        var files = await catalog.GetModFilesAsync(ct);
                        var families = LogicalModFamilies.Build(mods, files, run.FirstGame);
                        var matching = families.Where(f => f.DisplayName == name).ToArray();
                        state = $"Search present={currentSearch is not null}, query matches={query?.Current.Value == name}, " +
                            $"window enabled={window.Current.IsEnabled}, library present={grid is not null}, realized rows={realized}, " +
                            $"catalog families={families.Count}, matching families={matching.Length}, matching members={matching.Sum(f => f.Members.Count)}, " +
                            $"rows=[{string.Join(" | ", realizedNames)}], selectable={selectable}, " +
                            $"checkboxes=[{string.Join(" | ", checkboxes)}], owned windows=[{string.Join(" | ", windows)}]";
                    }
                    finally { Microsoft.Data.Sqlite.SqliteConnection.ClearPool(pool); }
                }
                catch (Exception diagnosticFailure) { state = $"Diagnostic read failed ({diagnosticFailure.GetType().Name})"; }
                throw new TimeoutException($"{error.Message}. {state}.", error);
            }
        }
        async Task<TogglePattern> Part(string name)
        {
            var part = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.CheckBox &&
                e.Current.Name == name && e.Current.IsEnabled), UiTimeout, "family option " + name, ct);
            return (TogglePattern)part.GetCurrentPattern(TogglePattern.Pattern);
        }
        async Task Expect(string name, bool on) =>
            Assert.Equal(on ? ToggleState.On : ToggleState.Off, (await Part(name)).Current.ToggleState);

        async Task VerifyFiles(string label, bool lantern)
        {
            var expected = ChoiceFiles.ToDictionary(file => Path.Combine(mount, file),
                file => original[Path.Combine(run.InstalledRoot, ChoiceTitle, label, file)]);
            foreach (var part in AddonLabels.Where(p => p != "Lantern" || lantern))
                expected.Add(Path.Combine(mount, part + ".pak"), Encoding.UTF8.GetBytes("kit " + part));
            await WaitForAsync(() => expected.All(file => File.Exists(file.Key) && File.ReadAllBytes(file.Key).SequenceEqual(file.Value))
                && (lantern || !File.Exists(Path.Combine(mount, "Lantern.pak"))), UiTimeout, "selected bytes in real profile mount", ct);
            Assert.Equal(baseline.Concat(expected.Keys).Order(StringComparer.Ordinal),
                Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        }

        await OpenFamily(ChoiceTitle);
        await WaitForElementAsync(() => Find(window, element => element.Current.ControlType == ControlType.Text &&
            element.Current.Name.StartsWith("Choose one", StringComparison.OrdinalIgnoreCase)), UiTimeout, "one-of option label", ct);
        await Expect("NO_Cape", false);
        await Expect("WITH_Cape", false);
        (await Part("NO_Cape")).Toggle();
        (await Part("WITH_Cape")).Toggle();
        await Expect("NO_Cape", false);
        await Expect("WITH_Cape", true);

        await WaitForAsync(() => ChoiceFiles.All(file =>
            File.Exists(Path.Combine(mount, file)) &&
            File.ReadAllBytes(Path.Combine(mount, file)).SequenceEqual(original[Path.Combine(run.InstalledRoot, ChoiceTitle, "WITH_Cape", file)])),
            UiTimeout, "automatic apply committed the selected Cape files before choosing another family", ct);

        await OpenFamily(AddonTitle);
        (await Part("Main")).Toggle();
        (await Part("Compass")).Toggle();
        (await Part("Lantern")).Toggle();
        await Expect("Compass", true);
        await Expect("Lantern", true);
        (await Part("Lantern")).Toggle();
        await Expect("Compass", true);
        await VerifyFiles("WITH_Cape", false);

        // Process.MainWindowHandle can still refer to the startup splash. Close the
        // actual WPF window whose controls served this fixture, then require process exit.
        var ownerWindow = await WaitForMainWindowAsync(app,ct);
        Assert.Equal(app.Id,ownerWindow.Current.ProcessId);
        await WaitForAsync(()=>ownerWindow.Current.IsEnabled &&
            Find(ownerWindow,e=>e.Current.Name=="Current operation phase" && !e.Current.IsOffscreen) is null,
            UiTimeout,"transaction overlay closed before restart",ct);
        var handle=(nint)ownerWindow.Current.NativeWindowHandle;
        Assert.NotEqual(0u,GetWindowThreadProcessId(handle,out var pid));
        Assert.Equal(app.Id,pid);
        Assert.NotEqual(0,PostCloseMessage(handle,0x0010,0,0)); // WM_CLOSE, equivalent to the title-bar close.
        try { await app.WaitForExitAsync(ct).WaitAsync(UiTimeout, ct); }
        catch (TimeoutException)
        {
            var windows=AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,app.Id))
                .Cast<AutomationElement>().Take(8).Select(element=>element.Current.ClassName + " " + element.Current.Name).ToArray();
            var diagnostics=Path.Combine(Path.GetDirectoryName(run.LaunchReceipt)!,"diagnostics");
            var logs=Directory.Exists(diagnostics) ? Directory.EnumerateFiles(diagnostics,"*.log",SearchOption.AllDirectories)
                .Take(8).SelectMany(file=>File.ReadLines(file).TakeLast(30))
                .Where(line=>line.Contains("OnExit",StringComparison.OrdinalIgnoreCase) || line.Contains("Dispose",StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("exception",StringComparison.OrdinalIgnoreCase)).TakeLast(12) : [];
            throw new TimeoutException($"The owned Manager process {app.Id} did not exit. Windows: {string.Join(" | ",windows)}; shutdown: {string.Join(" | ",logs)}");
        }
        app.Dispose();
        var reopened = run.Launch();
        window = await WaitForMainWindowAsync(reopened, ct);
        await OpenFamily(ChoiceTitle);
        await Expect("WITH_Cape", true);
        await Expect("NO_Cape", false);
        (await Part("NO_Cape")).Toggle();
        await Expect("WITH_Cape", false);
        await OpenFamily(AddonTitle);
        await Expect("Main", true);
        await Expect("Compass", true);
        await Expect("Lantern", false);
        await VerifyFiles("NO_Cape", false);
        (await Part("Lantern")).Toggle();
        await VerifyFiles("NO_Cape", true);
        await SelectModsTabAsync(window, ct);
        Invoke(await WaitForElementAsync(() => FindButton(window, "Undo Last Applied Change", enabled: true), UiTimeout, "undo last family choice", ct));
        await VerifyFiles("NO_Cape", false);
        foreach (var source in original) Assert.Equal(source.Value, File.ReadAllBytes(source.Key));
    }
}
