using MhwModManager.App.ViewModels;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class ActiveInboxStageTests
{
    [Fact]
    public Task Running_apply_commits_captured_state_then_newer_toggle_reaches_disk() =>
        IsolatedWpfSession.RunAsync(async (services,ct)=>
        {
            var folder=Path.Combine(services.Paths.ModsRoot,"Concurrent Outfit");
            var payload=Path.Combine(folder,services.Paths.Game.ModRootRelativePath,"outfit.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
            await File.WriteAllTextAsync(payload,"known outfit",ct);
            await services.Catalog.RefreshFoldersAsync(ct);
            var mod=Assert.Single(await services.Database.GetModsAsync(ct));
            var row=new ModRowViewModel(Assert.Single(LogicalModFamilies.Build([mod])),()=>{});
            using var vm=new MainWindowViewModel(services){ ConfirmBeforeApply=false };
            vm.Mods.Add(row);
            row.StagedEnabled=true;
            bool intervened=false;
            vm.Conflicts.CollectionChanged+=(_,_)=>{
                if(intervened)return;
                intervened=true;
                row.StagedEnabled=false; // The command has already built the ON plan.
            };
            await vm.ApplyCommand.ExecuteAsync(null);
            Assert.True(intervened);
            var target=Path.Combine(services.Paths.Game.LiveModRoot,"outfit.bin");
            Assert.True(File.Exists(target),vm.StatusText);
            Assert.Equal("known outfit",File.ReadAllText(target));
            Assert.True(Assert.Single(await services.Database.GetModsAsync(ct)).Enabled);
            Assert.True(row.HasStagedChanges);
            Assert.False(row.StagedEnabled);
            await vm.ApplyCommand.ExecuteAsync(null);
            Assert.False(File.Exists(target),vm.StatusText);
            Assert.False(Assert.Single(await services.Database.GetModsAsync(ct)).Enabled);
            Assert.False(row.HasStagedChanges);
            Assert.Equal("known outfit",File.ReadAllText(payload));
        });

    [Fact]
    public void Explicit_roundtrip_during_discovery_survives_new_catalog_baseline()
    {
        var one=new ModDescriptor("one","One","One","one",false,10);
        var other=new ModDescriptor("other","Other","Other","other",false,20);
        var edits=new Dictionary<string,(bool enabled,int priority)>(PathRules.Comparer);
        var original=new ModRowViewModel(new LogicalModFamily("f","F","F",[one,other],null),()=>{},null,(id,state)=>edits[id]=state);
        original.SetMemberEnabled(one.Id,true);
        original.SetMemberEnabled(one.Id,false);
        Assert.False(original.HasStagedChanges);
        Assert.Equal(one.Id,Assert.Single(edits).Key);
        var refreshed=new ModRowViewModel(new LogicalModFamily("f","F","F",[one with {Enabled=true},other with {Enabled=true}],null),()=>{},edits);
        Assert.False(Assert.Single(refreshed.StagedChanges()).Value.enabled);
        Assert.True(refreshed.ExpandStage()[other.Id].enabled);
    }

    [Fact]
    public void A_second_user_edit_must_remain_pending_after_the_earlier_transaction_commits()
    {
        var mod=new ModDescriptor("one","One","One","src",false,10);
        var row=new ModRowViewModel(new LogicalModFamily("one","One","One",[mod],null),()=>{});
        row.StagedEnabled=true;
        var transaction=row.ExpandStage().ToDictionary(p=>p.Key,p=>(p.Value.enabled,p.Value.priority));
        row.StagedEnabled=false;
        row.CommitApplied(transaction);
        Assert.True(transaction["one"].enabled);
        Assert.True(row.HasStagedChanges);
        Assert.False(Assert.Single(row.StagedChanges()).Value.enabled);
    }

    [Fact]
    public void Reload_keeps_only_pending_member_edits_and_preserves_new_catalog_defaults()
    {
        var changed = new ModDescriptor("old", "Old", "Old", "old", false, 10);
        var unchanged = new ModDescriptor("same", "Same", "Same", "same", true, 20);
        var added = new ModDescriptor("new", "New", "New", "new", false, 30);
        var original = new ModRowViewModel(new LogicalModFamily("f", "F", "F", [changed, unchanged], null), () => { });
        original.ApplyProfileState(new Dictionary<string, (bool, int)> { ["old"] = (true, 70), ["same"] = (true, 20) });
        var pending = original.StagedChanges().ToDictionary(x => x.Key, x => x.Value);

        // A new authoritative value on an untouched member takes precedence over its old projection.
        var family = new LogicalModFamily("f", "F", "F", [changed, unchanged with { Priority = 25 }, added], null);
        var refreshed = new ModRowViewModel(family, () => { }, pending);
        Assert.Equal((true, 70), refreshed.ExpandStage()["old"]);
        Assert.Equal((true, 25), refreshed.ExpandStage()["same"]);
        Assert.Equal((false, 30), refreshed.ExpandStage()["new"]);
        Assert.Single(refreshed.StagedChanges());
        refreshed.DiscardStaged();
        Assert.Equal((false, 10), refreshed.ExpandStage()["old"]);
    }
}
