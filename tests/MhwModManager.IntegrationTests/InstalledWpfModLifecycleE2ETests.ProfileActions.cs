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
        var animationBox = await WaitForElementAsync(() => Find(window, item => item.Current.ControlType == ControlType.CheckBox &&
            item.Current.Name == "Interface animations"), UiTimeout, "animation setting", ct);
        var animations = (TogglePattern)animationBox.GetCurrentPattern(TogglePattern.Pattern);
        bool SavedAnimations()
        {
            using var settings = JsonDocument.Parse(File.ReadAllBytes(run.SettingsPath));
            return settings.RootElement.GetProperty("uiAnimationsEnabled").GetBoolean();
        }
        Assert.False(SavedAnimations());
        Assert.Equal(ToggleState.Off, animations.Current.ToggleState);
        animations.Toggle();
        await WaitForAsync(() => animations.Current.ToggleState == ToggleState.On && SavedAnimations(),
            UiTimeout, "enabled animation setting persisted", ct);
        animations.Toggle();
        await WaitForAsync(() => animations.Current.ToggleState == ToggleState.Off && !SavedAnimations(),
            UiTimeout, "original animation setting restored on disk", ct);
        passed.Add("Toggle interface animations, verify settings on disk and restore");

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
    }
}
