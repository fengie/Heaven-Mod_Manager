using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
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
            var search = await WaitForElementAsync(() => Find(window, e =>
                e.Current.ControlType == ControlType.Edit && e.Current.Name == "Search mods"), UiTimeout, "Mods search", ct);
            ((ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern)).SetValue(name);
            var box = await WaitForElementAsync(() => Find(window, e =>
                e.Current.ControlType == ControlType.CheckBox && e.Current.Name == name), UiTimeout, "family " + name, ct);
            var row = box;
            while (row is not null && row.Current.ControlType != ControlType.DataItem)
                row = TreeWalker.RawViewWalker.GetParent(row);
            Assert.NotNull(row);
            ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        }
        async Task<TogglePattern> Part(string name)
        {
            var part = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.CheckBox &&
                e.Current.Name == name && e.Current.IsEnabled), UiTimeout, "family option " + name, ct);
            return (TogglePattern)part.GetCurrentPattern(TogglePattern.Pattern);
        }
        async Task Expect(string name, bool on) =>
            Assert.Equal(on ? ToggleState.On : ToggleState.Off, (await Part(name)).Current.ToggleState);

        async Task Preview(int add, int replace)
        {
            await SelectModsTabAsync(window, ct);
            Invoke(await WaitForElementAsync(() => FindButton(window, "Preview Mod Changes", enabled: true), UiTimeout, "preview family", ct));
            await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Text &&
                e.Current.Name.StartsWith("Dry run passed.", StringComparison.Ordinal) &&
                e.Current.Name.Contains($"{add} add", StringComparison.Ordinal) &&
                e.Current.Name.Contains($"{replace} replace", StringComparison.Ordinal) &&
                e.Current.Name.Contains("0 remove", StringComparison.Ordinal) && e.Current.Name.Contains("0 restore", StringComparison.Ordinal)),
                UiTimeout, "exact selected family plan", ct);
        }
        async Task Apply()
        {
            Invoke(await WaitForElementAsync(() => FindButton(window, "Apply Changes", enabled: true),
                UiTimeout, "apply selected family parts", ct));
            await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.Text &&
                e.Current.Name.StartsWith("Applying Mod Changes finished in", StringComparison.Ordinal)),
                UiTimeout, "family deployment completed", ct);
        }
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

        await OpenFamily(AddonTitle);
        (await Part("Main")).Toggle();
        (await Part("Compass")).Toggle();
        (await Part("Lantern")).Toggle();
        await Expect("Compass", true);
        await Expect("Lantern", true);
        (await Part("Lantern")).Toggle();
        await Expect("Compass", true);
        await Preview(5, 0);
        Assert.Equal(baseline, Directory.GetFiles(gameRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        await Apply();
        await VerifyFiles("WITH_Cape", false);

        Assert.True(app.CloseMainWindow());
        await app.WaitForExitAsync(ct).WaitAsync(UiTimeout, ct);
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
        (await Part("Lantern")).Toggle();
        await Preview(1, 3);
        await VerifyFiles("WITH_Cape", false);
        await Apply();
        await VerifyFiles("NO_Cape", true);
        await SelectModsTabAsync(window, ct);
        Invoke(await WaitForElementAsync(() => FindButton(window, "Undo Last Applied Change", enabled: true), UiTimeout, "undo family choice", ct));
        await VerifyFiles("WITH_Cape", false);
        foreach (var source in original) Assert.Equal(source.Value, File.ReadAllBytes(source.Key));
    }
}
