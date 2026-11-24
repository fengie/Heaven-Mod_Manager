using System.Windows.Automation;
using MhwModManager.Storage;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed partial class InstalledWpfModLifecycleE2ETests
{
    private static async Task<(AutomationElement Dialog, Task Invocation)> OpenWorkflowAsync(
        AutomationElement main, CancellationToken ct)
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

        return (dialog, invoke);
    }

    // UIA may disable a tab between reading IsEnabled and Select while WPF
    // refreshes the workflow. Retry only this idempotent selection attempt;
    // the caller reacquires the tab and still requires selected state before
    // continuing. A persistently disabled tab remains a bounded test failure.
    internal static bool TrySelectWorkflowTab(Func<bool> selectAndVerify)
    {
        try
        {
            return selectAndVerify();
        }
        catch (ElementNotEnabledException)
        {
            return false;
        }
    }

    private static async Task ExerciseWorkflowActionsAsync(DisposableWpfRun run, AutomationElement main,
        List<string> passed, CancellationToken ct)
    {
        var (dialog, invoke) = await OpenWorkflowAsync(main, ct);

        async Task<AutomationElement> Id(string id) => await WaitForElementAsync(() => Find(dialog,
            e => e.Current.AutomationId == id && e.Current.IsEnabled), UiTimeout, id, ct);
        async Task Tab(string name)
        {
            // Switching tabs can invalidate the old UIA element while the
            // workflow refreshes its data. Reacquire on each bounded attempt,
            // and verify the selected state instead of trusting a stale pattern.
            await WaitForAsync(() =>
            {
                var tab = Find(dialog, e => e.Current.ControlType == ControlType.TabItem &&
                    e.Current.Name == name && e.Current.IsEnabled);
                if (tab is null || !tab.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var found) ||
                    found is not SelectionItemPattern selection) return false;
                return TrySelectWorkflowTab(() =>
                {
                    if (!selection.Current.IsSelected) selection.Select();
                    return selection.Current.IsSelected;
                });
            }, UiTimeout, "workflow tab " + name, ct);
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
        var incomingName = "Added while workflow was open " + run.FirstGame.Id;
        ((ValuePattern)(await Id("ProfileName")).GetCurrentPattern(ValuePattern.Pattern)).SetValue(childName);
        await Tab("File Decisions");
        await new ProfileRepository(run.OpenCatalog()).SaveCurrentAsync(incomingName,ct);
        await Tab("Profiles & Compare");
        Assert.Equal(childName,Value(await Id("ProfileName")));
        await Choose("ParentProfile", incomingName);
        await Choose("ParentProfile", parentName);
        passed.Add("Workflow page reloaded a new external profile and preserved its unsaved setup name");
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

        await Tab("Portable Mod List");
        async Task Pick(string action, string submit, string path)
        {
            var button = await WaitForElementAsync(() => FindButton(dialog, action, enabled: true),
                UiTimeout, action, ct);
            var opening = Task.Run(() => Invoke(button), ct);
            var picker = await WaitForElementAsync(() => FindFileDialog(dialog.Current.ProcessId),
                UiTimeout, action + " file picker", ct);
            var field = await WaitForElementAsync(() => FindPickerField(picker, false), UiTimeout, "file name", ct);
            await WaitForElementAsync(() => FindButton(picker, submit, enabled: true), UiTimeout, submit, ct);
            await TypeNativePathAsync(field, path, dialog.Current.ProcessId, ct);
            await opening.WaitAsync(UiTimeout, ct);
        }
        async Task StatusContains(string message)
        {
            string latest = "";
            try
            {
                await WaitForAsync(() =>
                {
                    latest = Find(dialog, e => e.Current.AutomationId == "Status")?.Current.Name ?? "";
                    return latest.Contains(message, StringComparison.Ordinal);
                }, UiTimeout, message, ct);
            }
            finally
            {
                var evidence = Environment.GetEnvironmentVariable("MHW_GAME_UI_EVIDENCE");
                if (!string.IsNullOrEmpty(evidence))
                    File.AppendAllText(Path.Combine(evidence, $"portable-{run.FirstGame.Id}.txt"),
                        latest + Environment.NewLine);
            }
        }
        var recipe = Path.Combine(run.ManagerHome, "portable.mhwrecipe");
        await Pick("Export Current Mod List…", "Save", recipe);
        await StatusContains("Exported the current mod list:");
        using (var json = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(recipe)))
        {
            Assert.Equal(run.FirstGame.Id, json.RootElement.GetProperty("gameId").GetString());
            Assert.Contains(json.RootElement.GetProperty("mods").EnumerateArray(), m =>
                m.GetProperty("displayName").GetString() == primary && m.GetProperty("enabled").GetBoolean());
        }
        await Pick("Open Mod List…", "Open", recipe);
        await StatusContains("entries can be restored.");
        var recipeRows = await Id("RecipeRows");
        await WaitForElementAsync(() => Find(recipeRows, e => e.Current.Name == primary), UiTimeout, "exported package in review", ct);
        passed.Add("Save and reopen this game's exported mod list using the native file pickers");

        var handoff = Path.Combine(run.ManagerHome, "portable.vortexhandoff.json");
        if (MhwModManager.Automation.VortexInteropContract.TryGetGame(run.FirstGame) is not null)
        {
            await Pick("Export Vortex Handoff…", "Save", handoff);
            await StatusContains("Exported a credential-free Vortex handoff:");
            await Pick("Open Vortex Handoff…", "Open", handoff);
            await StatusContains("Vortex handoff entries can be restored.");
            passed.Add("Export and open a configured Vortex handoff using both native pickers");
        }
        else
        {
            await Click("Export Vortex Handoff…");
            await StatusContains("Vortex handoff is not configured");
            await File.WriteAllTextAsync(handoff, """{"format":1,"game":{"vortexGameId":"monsterhunterworld","modPath":"nativePC","executable":"MonsterHunterWorld.exe"},"mods":[]}""", ct);
            await Pick("Open Vortex Handoff…", "Open", handoff);
            await StatusContains("not configured for game profile '" + run.FirstGame.Id + "'");
            passed.Add("Refuse both Vortex export and a foreign Vortex file on an unconfigured profile");
        }

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

    // Native common dialogs maintain their own selected item and edit buffer. Simulate
    // typing into the focused edit and accepting it, as the Windows user would.
    private static async Task TypeNativePathAsync(AutomationElement field, string path, int pid, CancellationToken ct, bool submit = true)
    {
        field.SetFocus();
        await WaitForAsync(() => AutomationElement.FocusedElement.Current.ProcessId == pid &&
            field.Current.HasKeyboardFocus, UiTimeout, "focused native path field", ct);
        var text = new System.Text.StringBuilder();
        foreach (var ch in path)
        {
            if ("+^%~()[]{}".Contains(ch)) text.Append('{').Append(ch).Append('}');
            else text.Append(ch);
        }
        dynamic keys = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        try
        {
            keys.SendKeys("^a");
            keys.SendKeys(text.ToString());
            await WaitForAsync(() => ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value
                .Equals(path, StringComparison.OrdinalIgnoreCase), UiTimeout, "typed native path", ct);
            Assert.Equal(pid, AutomationElement.FocusedElement.Current.ProcessId);
            if (submit) keys.SendKeys("{ENTER}");
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject((object)keys); }
    }
}
