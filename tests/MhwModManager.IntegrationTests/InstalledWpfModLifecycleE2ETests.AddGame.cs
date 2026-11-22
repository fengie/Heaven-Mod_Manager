using System.Windows.Automation;
using MhwModManager.Core;
using MhwModManager.Filesystem;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    private static async Task ExerciseAddGameAsync(DisposableWpfRun run, AutomationElement window,
        List<string> passed, CancellationToken ct)
    {
        int pid = window.Current.ProcessId;
        var registry = new GameProfileRegistry(Path.Combine(run.ManagerHome, "State"));
        var before = registry.Load().ToArray();
        const string pickerTitle = "Select a game executable";

        async Task<Task> OpenAddGameAsync()
        {
            var dashboard = await WaitForElementAsync(() => Find(window, element =>
                element.Current.ControlType == ControlType.TabItem && element.Current.Name == "Dashboard"),
                UiTimeout, "Dashboard game management", ct);
            ((SelectionItemPattern)dashboard.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            var add = await WaitForElementAsync(() => FindButton(window, "Add Game", enabled: true),
                UiTimeout, "Add Game control", ct);
            return Task.Run(() => Invoke(add), ct);
        }
        async Task<AutomationElement> Dialog(string title) => await WaitForElementAsync(
            () => FindProcessDialog(pid, title), UiTimeout, title, ct);
        async Task Choose(AutomationElement dialog, string choice) => Invoke(await WaitForElementAsync(
            () => FindButton(dialog, choice, enabled: true), UiTimeout, choice, ct));
        async Task<TogglePattern> PrimaryToggle()
        {
            await SelectModsTabAsync(window, ct);
            var box = await WaitForElementAsync(() => FindModToggle(window, "E2E Inbox Fixture"),
                UiTimeout, "managed package before adding game", ct);
            return (TogglePattern)box.GetCurrentPattern(TogglePattern.Pattern);
        }

        var original = SeedDiscoverySample(run.FirstGame.GameRoot,
            run.FirstGame.AdapterId == "unreal-paks" ? run.FirstGame.LiveModRoot : null, "Original scope fixture");
        var other = SeedDiscoverySample(run.SecondGame.GameRoot, null, "Other scope fixture");
        await VerifyDiscoveryScopeAsync(window, run.FirstGame, original, other,
            Path.Combine(run.FirstGame.GameRoot, "Mods", "inventory-test", "mod.json"),
            run.FirstGame.IsMonsterHunterWorld || run.FirstGame.AdapterId == "unreal-paks" ? run.LiveModPath : null, ct);
        passed.Add("Map the current package, other game and evidence-only file to distinct library statuses");

        var opening = await OpenAddGameAsync();
        await Choose(await Dialog(pickerTitle), "Cancel");
        await opening.WaitAsync(UiTimeout, ct);
        await WaitForAsync(() => FindProcessDialog(pid, pickerTitle) is null, UiTimeout, "cancel game picker", ct);
        Assert.Equal(before, registry.Load().ToArray());
        Assert.Equal(run.FirstGame.Id, registry.GetActive()?.Id);
        passed.Add("Cancel Add Game picker and retain original registry and selection");

        var toggle = await PrimaryToggle();
        Assert.Equal(ToggleState.On, toggle.Current.ToggleState);
        var externalEdit=new byte[]{42,14,9};
        File.WriteAllBytes(run.LiveModPath,externalEdit);
        toggle.Toggle();
        await WaitForAsync(() => toggle.Current.ToggleState == ToggleState.Off, UiTimeout, "stage original package off", ct);
        opening = await OpenAddGameAsync();
        await Choose(await Dialog("Pending Mod Changes"), "No");
        await opening.WaitAsync(UiTimeout, ct);
        Assert.Null(FindProcessDialog(pid, pickerTitle));
        Assert.Equal(before, registry.Load().ToArray());
        Assert.Equal(run.FirstGame.Id, registry.GetActive()?.Id);
        toggle = await PrimaryToggle();
        Assert.Equal(ToggleState.Off, toggle.Current.ToggleState);
        Assert.Equal(externalEdit, File.ReadAllBytes(run.LiveModPath));
        File.WriteAllBytes(run.LiveModPath,run.ModBytes);
        passed.Add("Decline Add Game while staged and retain pending choice and deployed bytes");

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(run.ManagerHome)!, "Added fixture game"));
        var addedSample = SeedDiscoverySample(folder.FullName, null, "Added scope fixture");
        var executable = Path.Combine(folder.FullName, "AddedFixture.exe");
        File.Copy(run.FirstGame.ExecutablePath, executable);
        opening = await OpenAddGameAsync();
        await Choose(await Dialog("Pending Mod Changes"), "Yes");
        var picker = await Dialog(pickerTitle);
        var filename = await WaitForElementAsync(() => FindPickerField(picker, false), UiTimeout, "game executable filename", ct);
        ((ValuePattern)filename.GetCurrentPattern(ValuePattern.Pattern)).SetValue(executable);
        await Choose(picker, "Open");
        await opening.WaitAsync(UiTimeout, ct);
        await WaitForAsync(() => registry.GetActive() is { } active &&
            PathRules.Comparer.Equals(active.ExecutablePath, executable) && ReadActiveGame(window) == active.DisplayName,
            UiTimeout, "added game committed and displayed in the original window", ct);
        var added = Assert.Single(registry.Load(), game => PathRules.Comparer.Equals(game.ExecutablePath, executable));
        Assert.Equal(before.Length + 1, registry.Load().Count);
        foreach (var previous in before) Assert.Contains(previous, registry.Load());
        Assert.Equal(added.Id, registry.GetActive()?.Id);
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.LiveModPath));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        passed.Add("Add a new game through the native executable picker and switch in place while preserving all original registrations and files");
        await VerifyDiscoveryScopeAsync(window, added, addedSample, original, null, null, ct);
        passed.Add("Switch Mods to the newly registered game while Installed files keeps the prior game visible");
    }
}
