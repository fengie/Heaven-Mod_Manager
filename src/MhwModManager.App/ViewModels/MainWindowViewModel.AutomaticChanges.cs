using System.IO;
using System.Windows;
using System.Windows.Threading;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool discoveryRequested;
    private bool retryDiscoveryOnActivation;
    private bool rulesNeedApply;

    private void RequestRuleApply()
    {
        using var trace = MasterDebugLog.BeginMethod();
        rulesNeedApply = true;
        Changed();
    }
    private bool automaticChangesScheduled;
    private Task automaticChangeWork=Task.CompletedTask;

    private Dictionary<string,(bool enabled,int priority)>? discoveryUserEdits;

    private void RecordUserChoice(string id,(bool enabled,int priority) state)
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        if(discoveryUserEdits is not null) discoveryUserEdits[id]=state;
        OnModChoiceChanged();
    }

    private void RequestDiscoveryCheck()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        discoveryRequested = true;
        ScheduleAutomaticChanges();
    }

    internal void ResumeDeferredDiscovery()
    {
        using var __mhwTrace=MasterDebugLog.BeginMethod();
        if (retryDiscoveryOnActivation)
        {
            retryDiscoveryOnActivation=false;
            discoveryRequested=true;
        }
        ScheduleAutomaticChanges();
    }

    private void OnModChoiceChanged()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (suppressChanged) return;
        // A user choice is only staged. Do not dispatch a deployment or retry it on activation.
        OnPropertyChanged(nameof(HasPendingDeployment));
    }

    private void PreserveDeniedAutomaticChange(string operationName)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (operationName == "deployment.discovered")
        {
            // DrainAutomaticChangesAsync consumed discoveryRequested before
            // RunBusy could acquire the foreground gate. The catalog still
            // contains the pending discovery IDs; retry on the next real
            // window activation instead of repeatedly dispatching while busy.
            retryDiscoveryOnActivation = true;
            StatusText = "New mods are waiting for the current operation to finish. They will be checked when the window is activated.";
            return;
        }
        if (operationName != "deployment.apply" || (StagedCount == 0 && !rulesNeedApply)) return;
        StatusText = "Your mod changes are still pending. Apply after the current operation finishes.";
    }

    private void ScheduleAutomaticChanges()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (automaticChangesScheduled || !discoveryRequested || HasPendingDeployment ||
            busyCts is not null || backgroundCts.IsCancellationRequested || Application.Current is null) return;
        automaticChangesScheduled = true;
        automaticChangeWork=Application.Current.Dispatcher.InvokeAsync(DrainAutomaticChangesAsync, DispatcherPriority.Background).Task.Unwrap();
    }

    private async Task DrainAutomaticChangesAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            if (busyCts is not null || backgroundCts.IsCancellationRequested) return;
            if (discoveryRequested && !HasPendingDeployment && !backgroundCts.IsCancellationRequested)
            {
                discoveryRequested = false;
                var pending = await s.Database.GetSettingsByPrefixAsync("discovery.pending:",backgroundCts.Token);
                if (pending.Count > 0 && busyCts is null && !HasPendingDeployment && !disposed && !backgroundCts.IsCancellationRequested)
                    await RunBusy("deployment.discovered","Checking new mods","Checking ownership, compatibility and game files…",false,async ct =>
                    {
                        discoveryUserEdits=new(PathRules.Comparer);
                        try
                        {
                            var applied = await Task.Run(()=>ApplyDiscoveredAsync(s,pending.Values.ToArray(),ct),ct);
                            if (applied is null) { retryDiscoveryOnActivation=true; StatusText="New mods are waiting for an available game installation."; return; }
                            if (applied.Value > 0)
                            {
                                await ReloadMods(ct,preserveStagedChanges:true);
                                await RefreshAnalysis(ct);
                                await RefreshActivity(ct);
                                StatusText=$"Enabled {applied.Value} newly discovered mod(s).";
                            }
                            else StatusText="Discovered packages remain in Mods for review or explicit selection.";
                        }
                        finally { discoveryUserEdits=null; }
                    });
                else if(pending.Count>0) discoveryRequested=true;
            }
        }
        catch (OperationCanceledException) when (backgroundCts.IsCancellationRequested) { }
        catch (Exception ex)
        {
            StatusText = $"Automatic mod check paused. {ex.Message}";
            MasterDebugLog.Write("AUTO-DEPLOY","Automatic mod check stopped.",ex);
        }
        finally
        {
            automaticChangesScheduled = false;
            // Do not auto-deploy user choices, even after reactivation or a blocked attempt.
            if (discoveryRequested && !HasPendingDeployment) ScheduleAutomaticChanges();
        }
    }

    internal static async Task<int?> ApplyDiscoveredAsync(AppServices s,string[] ids,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!File.Exists(s.Paths.Game.ExecutablePath) || s.ProcessGuard.GetKnownBlockers().Count > 0)
        {
            return null;
        }
        var pending = ids.ToHashSet(PathRules.Comparer);
        pending.IntersectWith((await s.Database.GetSettingsByPrefixAsync("discovery.pending:",ct)).Values);
        if(pending.Count==0) return 0;
        var mods = await s.Database.GetModsAsync(ct);
        var protectedIds = await s.Database.GetDiscoveryProtectedIdsAsync(ct);
        var candidates = mods.Where(m => pending.Contains(m.Id) && !protectedIds.Contains(m.Id) && !m.Enabled && !m.IsSuperseded &&
            !m.RequiresLayoutReview && !m.NeedsRevalidation && m.FamilyId is null &&
            Directory.Exists(m.SourcePath)).Select(m => m.Id).ToHashSet(PathRules.Comparer);
        if (candidates.Count == 0)
        {
            await s.Database.FinishDiscoveryAsync(ids,ct);
            return 0;
        }
        var enabled = mods.Where(m=>m.Enabled).Select(m=>m.Id).ToArray();
        var excluded = mods.Where(m=>!m.Enabled && !candidates.Contains(m.Id)).Select(m=>m.Id).ToArray();
        var populate = new AutoPopulateService(s.PlannerSnapshots,s.Planner,s.Dependencies,s.Paths.GameRoot,s.Paths.Game,s.Blobs,s.Scanner);
        var selected = await RunAutoPopulateBuildAsync(token=>populate.BuildAsync(enabled,excluded,token),ct);
        var considered = new HashSet<string>(candidates,PathRules.Comparer);
        candidates.ExceptWith(selected.SharedExportIds);
        candidates.RemoveWhere(id=>!selected.State.TryGetValue(id,out var state) || !state.Enabled);
        var snapshot = await s.PlannerSnapshots.LoadAsync(ct);
        var byTarget = snapshot.Files.Where(f=>considered.Contains(f.ModId) || enabled.Contains(f.ModId,PathRules.Comparer))
            .ToLookup(f=>f.Path,PathRules.Comparer);
        var permitted = snapshot.Files.Where(f=>candidates.Contains(f.ModId)).GroupBy(f=>f.ModId,PathRules.Comparer)
            .Where(group=>group.All(f=>IsAutomaticDataFile(f) && byTarget[f.Path].Select(x=>x.ModId).Distinct(PathRules.Comparer).Count()==1
                && !Path.Exists(s.Executor.Destination(f.Path))))
            .Select(group=>group.Key).ToHashSet(PathRules.Comparer);
        if (permitted.Count > 0)
        {
            var stage = mods.ToDictionary(m=>m.Id,m=>(enabled:m.Enabled || permitted.Contains(m.Id),priority:m.Priority),PathRules.Comparer);
            var plan = s.Planner.Build(snapshot with
            {
                Mods=snapshot.Mods.Select(m=>stage.TryGetValue(m.Id,out var choice) ? m with {Enabled=choice.enabled} : m).ToArray()
            });
            if (!plan.IsBlocked && plan.Changes.Count>0 && plan.Changes.All(change=>change.Kind==ChangeKind.Add &&
                change.ProviderAfter is not null && permitted.Contains(change.ProviderAfter)) &&
                permitted.SetEquals(plan.Changes.Select(change=>change.ProviderAfter!)) &&
                (await s.Dependencies.ScanStageAsync(stage.Where(p=>p.Value.enabled).Select(p=>p.Key).ToHashSet(PathRules.Comparer),plan,ct)).All(d=>d.Ready))
            {
                plan = plan with { Changes=plan.Changes.Select(change=>change with {RequireAbsent=true}).ToArray() };
                var result = await s.Executor.ApplyAsync(plan,"Automatically install newly discovered mods",stage,ct:ct);
                if (!result.Success) throw new IOException(result.Message,result.Exception);
                await s.Database.FinishDiscoveryAsync(ids,ct);
                return permitted.Count;
            }
        }
        await s.Database.FinishDiscoveryAsync(ids,ct);
        return 0;
    }

    internal static bool IsAutomaticDataFile(ModFileDescriptor file)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return file.FileClass is FileClass.Texture or FileClass.Structural or FileClass.GameData ||
            Path.GetExtension(file.Path).ToLowerInvariant() is ".pak" or ".utoc" or ".ucas" or ".sig";
    }
}
