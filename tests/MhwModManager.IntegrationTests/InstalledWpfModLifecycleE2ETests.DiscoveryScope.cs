using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    private sealed record DiscoverySample(string Name, string EvidencePath, string PayloadPath, byte[] Payload);

    private static DiscoverySample SeedDiscoverySample(string gameRoot, string? unrealMount, string name)
    {
        byte[] payload = Encoding.UTF8.GetBytes("-- Disposable package " + name);
        string evidencePath, payloadPath;
        if (unrealMount is not null)
        {
            var directory = Directory.CreateDirectory(Path.Combine(unrealMount, "~mods"));
            evidencePath = payloadPath = Path.Combine(directory.FullName, name + ".pak");
        }
        else
        {
            var directory = Directory.CreateDirectory(Path.Combine(gameRoot, "Mods", name));
            evidencePath = Path.Combine(directory.FullName, "mod.json");
            File.WriteAllText(evidencePath, JsonSerializer.Serialize(new { name }));
            payloadPath = Path.Combine(directory.FullName, "fixture.lua");
        }
        File.WriteAllBytes(payloadPath, payload);
        return new DiscoverySample(name, evidencePath, payloadPath, payload);
    }

    private static async Task VerifyDiscoveryScopeAsync(AutomationElement window, GameProfile active,
        DiscoverySample current, DiscoverySample foreign, string? unverifiedEvidence, string? managedEvidence, CancellationToken ct)
    {
        var tab = await WaitForElementAsync(() => Find(window, element =>
            element.Current.ControlType == ControlType.TabItem && element.Current.Name == "Installed files"),
            UiTimeout, "Installed files tab", ct);
        ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        await WaitForElementAsync(() => Find(window, e => e.Current.Name == "Search installed files across all games"),
            UiTimeout, "all-games inventory scope", ct);
        var inventorySearch = await WaitForElementAsync(() => Find(window, e =>
            e.Current.ControlType == ControlType.Edit && e.Current.AutomationId == "LocalModSearch"),
            UiTimeout, "inventory filter", ct);
        var grid = await WaitForElementAsync(() => Find(window, e => e.Current.ControlType == ControlType.DataGrid &&
            Find(e, cell => cell.Current.ControlType == ControlType.HeaderItem && cell.Current.Name == "Library status") is not null),
            UiTimeout, "inventory library status", ct);
        var filter = (ValuePattern)inventorySearch.GetCurrentPattern(ValuePattern.Pattern);
        async Task CheckStatus(string path, string status)
        {
            filter.SetValue(path);
            await WaitForElementAsync(() => Find(grid, row => row.Current.ControlType == ControlType.DataItem &&
                Find(row, cell => cell.Current.Name == path) is not null &&
                Find(row, cell => cell.Current.Name.StartsWith(status, StringComparison.OrdinalIgnoreCase)) is not null),
                UiTimeout, status + " for " + Path.GetFileName(path), ct);
        }
        await CheckStatus(current.EvidencePath, "External reference");
        await CheckStatus(foreign.EvidencePath, "Other game");
        if (unverifiedEvidence is not null) await CheckStatus(unverifiedEvidence, "Evidence only");
        if (managedEvidence is not null) await CheckStatus(managedEvidence, "Managed deployment or package");
        filter.SetValue("");

        await SelectModsTabAsync(window, ct);
        await WaitForElementAsync(() => Find(window, e => e.Current.Name == "Managed and discovered mods for " + active.DisplayName),
            UiTimeout, "active Mods library scope", ct);
        var search = await WaitForElementAsync(() => Find(window, e =>
            e.Current.ControlType == ControlType.Edit && e.Current.Name == "Search mods"), UiTimeout, "Mods filter", ct);
        var query = (ValuePattern)search.GetCurrentPattern(ValuePattern.Pattern);
        var mods = await WaitForElementAsync(() => Find(window, e =>
            e.Current.ControlType == ControlType.DataGrid && e.Current.Name == "Mod library"), UiTimeout, "Mods library", ct);
        query.SetValue(current.Name);
        var match = await WaitForElementAsync(() => Find(mods, row => row.Current.ControlType == ControlType.DataItem &&
            row.Current.Name == current.Name), UiTimeout, "discovered package in ordinary Mods", ct);
        Assert.Null(Find(match, e => e.Current.ControlType == ControlType.CheckBox));
        query.SetValue(foreign.Name);
        await WaitForAsync(() => Find(mods, row => row.Current.ControlType == ControlType.DataItem) is null,
            UiTimeout, "other game's package absent from active Mods", ct);
        query.SetValue("");
        Assert.Equal(current.Payload, File.ReadAllBytes(current.PayloadPath));
        Assert.Equal(foreign.Payload, File.ReadAllBytes(foreign.PayloadPath));
    }
}
