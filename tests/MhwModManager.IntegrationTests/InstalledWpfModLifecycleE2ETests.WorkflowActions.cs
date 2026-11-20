using System.Windows.Automation;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    private static async Task ExerciseWorkflowActionsAsync(DisposableWpfRun run, AutomationElement main,
        List<string> passed, CancellationToken ct)
    {
        const string title = "Advanced Tools & Diagnostics";
        var dashboard = await WaitForElementAsync(() => Find(main, e => e.Current.ControlType == ControlType.TabItem &&
            e.Current.Name == "Dashboard"), UiTimeout, "dashboard", ct);
        ((SelectionItemPattern)dashboard.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        var tools = await WaitForElementAsync(() => Find(main, e => e.Current.Name == "More tools" &&
            e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _)), UiTimeout, "dashboard tools", ct);
        var expander = (ExpandCollapsePattern)tools.GetCurrentPattern(ExpandCollapsePattern.Pattern);
        if (expander.Current.ExpandCollapseState == ExpandCollapseState.Collapsed) expander.Expand();
        var open = await WaitForElementAsync(() => FindButton(main, title, enabled: true), UiTimeout, title, ct);
        var invoke = Task.Run(() => Invoke(open), ct);
        var pid = main.Current.ProcessId;
        var ownerWindow = new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window),
            new PropertyCondition(AutomationElement.NameProperty, title));
        var dialog = await WaitForElementAsync(() => main.FindFirst(TreeScope.Descendants, ownerWindow) ??
            AutomationElement.RootElement.FindFirst(TreeScope.Children, ownerWindow), UiTimeout, "advanced workflow dialog", ct);

        async Task<AutomationElement> Id(string id) => await WaitForElementAsync(() => Find(dialog,
            e => e.Current.AutomationId == id && e.Current.IsEnabled), UiTimeout, id, ct);
        async Task Tab(string name)
        {
            var tab = await WaitForElementAsync(() => Find(dialog, e => e.Current.ControlType == ControlType.TabItem &&
                e.Current.Name == name && e.Current.IsEnabled), UiTimeout, name, ct);
            ((SelectionItemPattern)tab.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        }
        async Task Click(string label) => Invoke(await WaitForElementAsync(() => FindButton(dialog, label, enabled: true),
            UiTimeout, label, ct));
        async Task Choose(string boxId, string itemName)
        {
            var box = await Id(boxId);
            var list = (ExpandCollapsePattern)box.GetCurrentPattern(ExpandCollapsePattern.Pattern);
            list.Expand();
            bool IsChoice(AutomationElement e) => e.Current.ControlType == ControlType.ListItem &&
                e.Current.Name.Contains(itemName, StringComparison.Ordinal);
            var item = await WaitForElementAsync(() => Find(box, IsChoice) ?? Find(dialog, IsChoice),
                UiTimeout, boxId + " choice " + itemName, ct);
            ((SelectionItemPattern)item.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
            if (list.Current.ExpandCollapseState == ExpandCollapseState.Expanded) list.Collapse();
        }
        static string Value(AutomationElement field) => ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;

        await Tab("Technical Game Details");
        var details = Value(await Id("AdapterDetails"));
        Assert.Contains(run.FirstGame.ExecutablePath, details, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(run.FirstGame.LiveModRoot, details, StringComparison.OrdinalIgnoreCase);
        passed.Add("Open Advanced Tools and verify active executable and mod roots");

        const string primary = "E2E Inbox Fixture";
        const string alternate = "Independent Verification";
        await Tab("File Decisions");
        var query = (ValuePattern)(await Id("PathFilter")).GetCurrentPattern(ValuePattern.Pattern);
        var files = await Id("Files");
        query.SetValue("absent_decision_fixture_947");
        await WaitForAsync(() => Find(files, e => e.Current.ControlType == ControlType.DataItem) is null,
            UiTimeout, "file decisions exclude unmatched search", ct);
        query.SetValue(primary);
        var row = await WaitForElementAsync(() => Find(files, e => e.Current.ControlType == ControlType.DataItem &&
            Find(e, c => c.Current.Name == primary) is not null), UiTimeout, "winning provider's file decision", ct);
        ((SelectionItemPattern)row.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
        var explanation = await Id("Explanation");
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(run.ModBytes));
        await WaitForAsync(() => Value(explanation).Contains(hash, StringComparison.OrdinalIgnoreCase) &&
            Value(explanation).Contains(primary, StringComparison.Ordinal), UiTimeout, "actual winner and file digest", ct);
        passed.Add("Filter File Decisions and read winning provider and SHA-256");

        await Tab("Profiles & Compare");
        var parentName = "Verified setup " + run.FirstGame.Id;
        var childName = "Child setup " + run.FirstGame.Id;
        await Choose("ParentProfile", parentName);
        var nameField = (ValuePattern)(await Id("ProfileName")).GetCurrentPattern(ValuePattern.Pattern);
        nameField.SetValue(childName);
        await Click("Save Pending Setup");
        await WaitForElementAsync(() => Find(dialog, e => e.Current.Name.StartsWith("Saved the pending setup.",
            StringComparison.Ordinal)), UiTimeout, "workflow saves inherited setup", ct);
        var db = run.OpenCatalog();
        try
        {
            var repository = new ProfileRepository(db);
            var profiles = await repository.ListAsync(ct);
            var parent = Assert.Single(profiles, p => p.Name == parentName);
            var child = Assert.Single(profiles, p => p.Name == childName);
            Assert.Equal(parent.Id, child.ParentId);
            var state = await repository.LoadAsync(child.Id, ct);
            var mods = await db.GetModsAsync(ct);
            Assert.True(state[Assert.Single(mods, m => m.DisplayName == primary).Id].enabled);
            Assert.False(state[Assert.Single(mods, m => m.DisplayName == alternate).Id].enabled);
        }
        finally
        {
            await using var connection = await db.OpenAsync(CancellationToken.None);
            Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        }
        await Choose("ProfileA", parentName);
        await Choose("ProfileB", childName);
        await Click("Compare");
        var comparison = await Id("ProfileDiff");
        await WaitForAsync(() => Value(comparison).Contains(parentName, StringComparison.Ordinal) &&
            Value(comparison).Contains(childName, StringComparison.Ordinal) &&
            Value(comparison).Contains("0 enabled · 0 disabled", StringComparison.Ordinal),
            UiTimeout, "comparison of inherited and parent setup", ct);
        passed.Add("Save inherited setup, verify its stored parent and mod choices, and compare profiles");

        await Tab("Safe Update");
        var apply = await WaitForElementAsync(() => FindButton(dialog, "Apply Update"), UiTimeout, "update action", ct);
        Assert.False(apply.Current.IsEnabled);
        await Choose("OldMod", primary);
        await Choose("NewMod", alternate);
        await Click("Preview Update");
        var update = await Id("UpdateDetails");
        await WaitForAsync(() => Value(update).Contains(primary, StringComparison.Ordinal) &&
            Value(update).Contains(alternate, StringComparison.Ordinal) &&
            Value(update).Contains("1 removed · 1 added", StringComparison.Ordinal),
            UiTimeout, "safe update previews a changed package path", ct);
        Assert.Equal(run.ModBytes, File.ReadAllBytes(run.LiveModPath));
        passed.Add("Verify unreviewed update disabled and preview one removed and one added file");

        ((WindowPattern)dialog.GetCurrentPattern(WindowPattern.Pattern)).Close();
        await invoke.WaitAsync(UiTimeout, ct);
        await WaitForAsync(() => main.Current.IsEnabled, UiTimeout, "return to active manager", ct);
    }
}
