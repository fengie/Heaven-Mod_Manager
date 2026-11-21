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

        var opening = await OpenAddGameAsync();
        await Choose(await Dialog(pickerTitle), "Cancel");
        await opening.WaitAsync(UiTimeout, ct);
        await WaitForAsync(() => FindProcessDialog(pid, pickerTitle) is null, UiTimeout, "cancel game picker", ct);
        Assert.Equal(before, registry.Load().ToArray());
        Assert.Equal(run.FirstGame.Id, registry.GetActive()?.Id);
        passed.Add("Cancel Add Game picker and retain original registry and selection");

        var toggle = await PrimaryToggle();
        Assert.Equal(ToggleState.On, toggle.Current.ToggleState);
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
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.LiveModPath));
        passed.Add("Decline Add Game while staged and retain pending choice and deployed bytes");

        var folder = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(run.ManagerHome)!, "Added fixture game"));
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
        foreach (var original in before) Assert.Contains(original, registry.Load());
        Assert.Equal(added.Id, registry.GetActive()?.Id);
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.LiveModPath));
        Assert.Equal(run.BaselineBytes, File.ReadAllBytes(run.SentinelPath));
        passed.Add("Add a new game through the native executable picker and switch in place while preserving all original registrations and files");
    }
}

