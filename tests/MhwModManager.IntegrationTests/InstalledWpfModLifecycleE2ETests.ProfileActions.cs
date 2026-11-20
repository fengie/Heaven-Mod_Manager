using System.Windows.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    private static async Task ExerciseProfileActionsAsync(GameProfile game, AutomationElement window,
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
    }
}
