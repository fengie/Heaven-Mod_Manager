using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Core;
using MhwModManager.Storage;
using MhwModManager.Diagnostics;
using MhwModManager.Automation;
using MhwModManager.Updater;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel:ObservableObject, IDisposable
{
    private readonly AppServices s;
    private CancellationTokenSource? busyCts;
    private CancellationTokenSource? searchCts;
    private readonly CancellationTokenSource backgroundCts=new();
    private readonly SemaphoreSlim metadataGate=new(1,1);
    private readonly SemaphoreSlim deferredPageGate=new(1,1);
    private bool profilesLoaded;
    private bool activityLoaded;
    private bool overlapsLoaded;
    private bool initialMetadataRefreshStarted;
    private int conflictPreviewGeneration;
    private int conflictPreviewLoadedGeneration=-1;
    private bool suppressChanged;
    private bool disposed;

    public ObservableRangeCollection<ModRowViewModel> Mods{get;}=[];
    public ICollectionView ModsView{get;}
    public ObservableRangeCollection<ConflictRow> Conflicts{get;}=[];
    public ProfilesPageViewModel ProfilesPage{get;}
    public ObservableRangeCollection<ProfileSummary> Profiles{get;}
    public ActivityPageViewModel Activity{get;}
    public ObservableRangeCollection<ActivityRow> ActivityRows{get;}
    public CoveragePageViewModel Coverage{get;}
    public ObservableRangeCollection<OutfitRow> OutfitRows{get;}
    public ObservableRangeCollection<ModIssueRow> IssueSuspects{get;}=[];
    public ObservableRangeCollection<AssetOverlapRow> OverlapRows{get;}=[];
    public GamesPageViewModel GamesPage{get;}
    public ObservableRangeCollection<GameProfile> Games{get;}

    [ObservableProperty]private string searchText="";
    [ObservableProperty]private string modViewMode="All";
    [ObservableProperty]private string planPreviewText="No change preview yet.";
    [ObservableProperty]private string statusText="Ready.";
    [ObservableProperty]private string footerText="Ready";
    [ObservableProperty]private string busyTitle="Working…";
    [ObservableProperty]private string busyDetail="";
    [ObservableProperty]private Visibility busyVisibility=Visibility.Collapsed;
    [ObservableProperty]private Visibility cancelVisibility=Visibility.Collapsed;
    [ObservableProperty]private int selectedTab;
    [ObservableProperty]private string newProfileName="";
    [ObservableProperty]private ProfileSummary? selectedProfile;
    [ObservableProperty]private ConflictRow? selectedConflict;
    [ObservableProperty]private ModRowViewModel? selectedMod;
    [ObservableProperty]private OutfitRow? selectedOutfit;
    [ObservableProperty]private bool criticalOperation;
    [ObservableProperty]private int unmanagedFileCount;
    [ObservableProperty]private GameProfile? selectedGame;
    [ObservableProperty]private AssetOverlapRow? selectedOverlap;
    [ObservableProperty]private EffectiveDecisionExplanation? selectedExplanation;
    [ObservableProperty]private string explainWhyStatus="Select a shared file, then choose Why This Mod Wins to see the reason.";

    public string UnmanagedAdoptionLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return !SupportsLiveAdoption?"Manual mod import is unavailable when the whole game folder is managed":UnmanagedFileCount==0?"No manual mod files found":$"{UnmanagedFileCount} manual mod file(s) can be brought into the manager";
        }
    }
    public bool SupportsLiveAdoption=>s.Paths.Game.IsMonsterHunterWorld||!string.IsNullOrWhiteSpace(s.Paths.Game.ModRootRelativePath);
    public bool HasSemanticCoverage=>s.Paths.Game.SupportsSemanticCoverage;
    public string WindowTitle=>$"Universal Mod Manager — {s.Paths.Game.DisplayName}";
    public string GameSupportText
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return s.Paths.Game.SupportTier==GameSupportTier.AdapterEnhanced?"Enhanced game support":"Standard folder-based support";
        }
    }
    public string CoverageTabLabel=>HasSemanticCoverage?"Outfits":"Coverage";
    public string CoverageTitle=>HasSemanticCoverage?"Outfit coverage":"Game coverage";
    public string CoverageSubtitle
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return HasSemanticCoverage?"See which mods provide each armor model and which one the game will use.":"Detailed game-specific coverage is not available for this game yet. Use Mods and Shared Files to inspect file-level results.";
        }
    }

    public string HeaderSummary
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"{EnabledCount} enabled • {FullyEffectiveCount+ComposedCount} working in game • {BlockerCount} need attention • {UpdateCount} update(s)";
        }
    }
    public string GamePathText=>$"{s.Paths.Game.DisplayName}: {s.Paths.GameRoot}";
    public int InstalledCount=>Mods.Count;
    public string InstalledCountLabel
    {
        get
        {
            using var __mhwTrace=MasterDebugLog.BeginMethod();
            return $"{InstalledCount} {(InstalledCount==1?"mod":"mods")}";
        }
    }
    public int SourcePackageCount=>Mods.Sum(x=>x.MemberCount);
    public int EnabledCount=>Mods.Count(x=>x.StagedEnabled!=false);
    public int StagedCount=>Mods.Count(x=>x.HasStagedChanges);
    public int BlockerCount=>Conflicts.Count;
    public int FullyEffectiveCount=>Mods.Count(x=>x.EffectiveState==EffectiveModState.FullyEffective);
    public int ComposedCount=>Mods.Count(x=>x.EffectiveState==EffectiveModState.PartiallyOverridden);
    public int RevalidationCount=>Mods.Count(x=>x.EffectiveState==EffectiveModState.NeedsRevalidation);
    public int IssueCount=>IssueSuspects.Count;
    public int UpdateCount=>Mods.Count(x=>x.HasUpdate);
    public int SupersededCount=>Mods.Count(x=>x.EffectiveState==EffectiveModState.FullySuperseded);
    public int VisibleModCount=>ModsView.Cast<object>().Count();
    public int StagedEnableCount=>Mods.Count(x=>x.WillEnable);
    public int StagedDisableCount=>Mods.Count(x=>x.WillDisable);
    public int OverlapCount=>OverlapRows.Count;
    public string StagedSummary
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return StagedCount==0?"No pending changes":$"{StagedCount} pending • {StagedEnableCount} turning on • {StagedDisableCount} turning off";
        }
    }
    public string AllViewLabel=>$"All ({Mods.Count})";
    public string EnabledViewLabel=>$"Enabled ({EnabledCount})";
    public string StagedViewLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"Pending ({StagedCount})";
        }
    }
    public string UpdatesViewLabel=>$"Updates ({UpdateCount})";
    public string IssuesViewLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"Problems ({IssueCount})";
        }
    }
    public string RevalidateViewLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"Needs check ({RevalidationCount})";
        }
    }
    public string SupersededViewLabel
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return $"Not used ({SupersededCount})";
        }
    }

    public MainWindowViewModel(AppServices services)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        s=services;
        var settings=s.Settings.Current;
        autoUpdateEnabled=settings.AutoUpdateEnabled;
        uiAnimationsEnabled=settings.UiAnimationsEnabled;
        rememberLastTab=settings.RememberLastTab;
        confirmBeforeApply=settings.ConfirmBeforeApply;
        confirmBeforeDiscardStaged=settings.ConfirmBeforeDiscardStaged;
        backgroundMetadataRefreshEnabled=settings.BackgroundMetadataRefreshEnabled;
        selectedTab=rememberLastTab?Math.Clamp(settings.LastSelectedTab,0,7):0;
        UiMotion.AnimationsEnabled=uiAnimationsEnabled;
        CurrentProgramBuildText=s.BuildIdentity.BuildNumber>0
            ? $"v{s.BuildIdentity.ProductVersion} · build {s.BuildIdentity.BuildNumber} · {s.BuildIdentity.ShortSha}"
            : $"v{s.BuildIdentity.ProductVersion} · development";
        ProgramUpdateStatus=!UpdateClientService.CanSelfUpdate(UpdateClientService.GetInstallRoot())
            ? "Self-update is disabled for this development/unmanaged installation."
            : autoUpdateEnabled
                ? "Automatic program updates are ready."
                : "Automatic program updates are off. Manual checks remain available.";
        Activity=new ActivityPageViewModel(s.PresentationReads);
        ActivityRows=Activity.Rows;
        Coverage=new CoveragePageViewModel(s.PresentationReads);
        OutfitRows=Coverage.Rows;
        ProfilesPage=new ProfilesPageViewModel(s.Profiles);
        Profiles=ProfilesPage.Rows;
        GamesPage=new GamesPageViewModel(s.GameRegistry);
        Games=GamesPage.Rows;
        var detailedDiagnostics=MasterDebugLog.DetailedDiagnosticsEnabled;
        if(detailedDiagnostics)
        {
            PropertyChanged += (_, args) => MasterDebugLog.Write("VM-PROPERTY", $"MainWindowViewModel property changed: {args.PropertyName ?? "<unknown>"}");
            Mods.CollectionChanged += (_, args) => MasterDebugLog.Write("VM-COLLECTION", $"Mods change={args.Action}; count={Mods.Count}");
            OutfitRows.CollectionChanged += (_, args) => MasterDebugLog.Write("VM-COLLECTION", $"OutfitRows change={args.Action}; count={OutfitRows.Count}");
            Profiles.CollectionChanged += (_, args) => MasterDebugLog.Write("VM-COLLECTION", $"Profiles change={args.Action}; count={Profiles.Count}");
            ActivityRows.CollectionChanged += (_, args) => MasterDebugLog.Write("VM-COLLECTION", $"ActivityRows change={args.Action}; count={ActivityRows.Count}");
            IssueSuspects.CollectionChanged += (_, args) => MasterDebugLog.Write("VM-COLLECTION", $"IssueSuspects change={args.Action}; count={IssueSuspects.Count}");
            OverlapRows.CollectionChanged += (_, args) => MasterDebugLog.Write("VM-COLLECTION", $"OverlapRows change={args.Action}; count={OverlapRows.Count}");
        }
        Conflicts.CollectionChanged += (_, args) =>
        {
            if(detailedDiagnostics)MasterDebugLog.Write("VM-COLLECTION", $"Conflicts change={args.Action}; count={Conflicts.Count}");
            NotifyConflictDerivedState();
        };
        ModsView=CollectionViewSource.GetDefaultView(Mods);
        ModsView.Filter=FilterMod;
        GamesPage.Refresh();
        SelectedGame=Games.FirstOrDefault(x=>x.Id.Equals(s.Paths.Game.Id,StringComparison.OrdinalIgnoreCase));
    }

    private void NotifyConflictDerivedState()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OnPropertyChanged(nameof(BlockerCount));
        OnPropertyChanged(nameof(HeaderSummary));
    }

    public async Task InitializeAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("startup.ui","Loading library","Reading indexed state and conflict graph…",false,async ct=>
        {
            await ReloadMods(ct);
            UnmanagedFileCount=SupportsLiveAdoption?await Task.Run(()=>s.Adoption.CountAsync(ct),ct):0;
            await RefreshAnalysis(ct);
        });
        _=AutoMetadataLoopAsync(backgroundCts.Token);
    }

    partial void OnSelectedTabChanged(int value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"tab={value}");
        if(RememberLastTab)s.Settings.Update(settings=>settings.LastSelectedTab=Math.Clamp(value,0,7));
        if(value==1&&!initialMetadataRefreshStarted&&BusyVisibility!=Visibility.Visible)
        {
            initialMetadataRefreshStarted=true;
            _=EnsureInitialMetadataLoadedAsync(backgroundCts.Token);
        }
        if(value==3)_=EnsureConflictPreviewsLoadedAsync(backgroundCts.Token);
        if(value is 4 or 5 or 6)_=EnsureDeferredPageLoadedAsync(value,backgroundCts.Token);
    }

    private async Task EnsureDeferredPageLoadedAsync(int tab,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"tab={tab}");
        try
        {
            await deferredPageGate.WaitAsync(ct);
            try
            {
                switch(tab)
                {
                    case 4 when !profilesLoaded:
                        await RefreshProfiles(ct);
                        profilesLoaded=true;
                        break;
                    case 5 when !activityLoaded:
                        await RefreshActivity(ct);
                        activityLoaded=true;
                        break;
                    case 6 when !overlapsLoaded:
                        await RefreshOverlaps(ct);
                        overlapsLoaded=true;
                        break;
                }
            }
            finally{deferredPageGate.Release();}
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(Exception ex)
        {
            MasterDebugLog.Write("DEFERRED-PAGE",$"Deferred tab load failed. tab={tab}",ex);
        }
    }

    private async Task EnsureInitialMetadataLoadedAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(ct.IsCancellationRequested)return;
        await RunBusy("metadata.initial-refresh","Refreshing mod information","Checking lineage, artwork, and update metadata now that the Mods page is in use…",true,async innerCt=>
        {
            await metadataGate.WaitAsync(innerCt);
            try
            {
                var result=await s.Nexus.RefreshAsync(false,innerCt);
                await ReloadMods(innerCt);
                await RefreshAnalysis(innerCt);
                MasterDebugLog.Write("AUTO-METADATA",$"Demand-loaded metadata refresh: nexus={result.ApiRecords}; apiVisuals={result.VisualsRefreshed}; localVisuals={result.LocalVisuals}; declaredVisuals={result.DeclaredVisuals}; publicVisuals={result.PublicVisuals}; updates={result.UpdatesAvailable}");
            }
            finally{metadataGate.Release();}
        });
    }

    private async Task EnsureConflictPreviewsLoadedAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var generation=conflictPreviewGeneration;
        if(conflictPreviewLoadedGeneration==generation||Conflicts.Count==0)return;
        var rows=Conflicts.ToArray();
        try
        {
            var enriched=await EnrichConflictPreviewsAsync(rows,ct);
            if(ct.IsCancellationRequested||SelectedTab!=3||generation!=conflictPreviewGeneration)return;
            await Application.Current.Dispatcher.InvokeAsync(()=>
            {
                if(SelectedTab!=3||generation!=conflictPreviewGeneration)return;
                Conflicts.ReplaceAll(enriched);
                conflictPreviewLoadedGeneration=generation;
            });
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
        catch(Exception ex)
        {
            MasterDebugLog.Write("CONFLICT-PREVIEWS","Deferred conflict preview loading failed; conflict decisions remain available without artwork.",ex);
        }
    }

    private async Task AutoMetadataLoopAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            using var timer=new PeriodicTimer(TimeSpan.FromHours(4));
            while(await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    if(!BackgroundMetadataRefreshEnabled)
                    {
                        MasterDebugLog.Write("AUTO-METADATA","Periodic refresh skipped because background metadata refresh is disabled in Settings.");
                        continue;
                    }
                    if(BusyVisibility==Visibility.Visible||CriticalOperation)
                    {
                        MasterDebugLog.Write("AUTO-METADATA","Periodic refresh skipped because a foreground/critical operation is active.");
                        continue;
                    }
                    if(!await metadataGate.WaitAsync(0,ct))
                    {
                        MasterDebugLog.Write("AUTO-METADATA","Periodic refresh skipped because another metadata refresh is already running.");
                        continue;
                    }
                    try
                    {
                        var result=await s.Nexus.RefreshAsync(false,ct);
                        if(!result.LiveSyncRan)continue;
                        await ReloadMods(ct);
                        await RefreshAnalysis(ct);
                        MasterDebugLog.Write("AUTO-METADATA",$"Periodic refresh: nexus={result.ApiRecords}; apiVisuals={result.VisualsRefreshed}; localVisuals={result.LocalVisuals}; declaredVisuals={result.DeclaredVisuals}; publicVisuals={result.PublicVisuals}; updates={result.UpdatesAvailable}");
                    }
                    finally{metadataGate.Release();}
                }
                catch(Exception ex) when(ex is HttpRequestException or IOException or UnauthorizedAccessException)
                {
                    MasterDebugLog.Write("AUTO-METADATA","Periodic metadata refresh failed; keeping cached metadata.",ex);
                }
            }
        }
        catch(OperationCanceledException){}
    }

    partial void OnUnmanagedFileCountChanged(int value)=>OnPropertyChanged(nameof(UnmanagedAdoptionLabel));
    partial void OnSelectedModChanged(ModRowViewModel? value)
    {
        if(value is null)return;
        _=LoadModVisualsAsync(value);
    }

    private async Task LoadModVisualsAsync(ModRowViewModel row)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"logicalMod={row.Id}");
        try
        {
            var paths=await s.Visuals.GetGalleryAsync(row.Members,16);
            await Application.Current.Dispatcher.InvokeAsync(()=>row.SetVisuals(paths));
        }
        catch(IOException ex){MasterDebugLog.Write("VISUALS",$"Gallery scan failed logicalMod={row.Id}",ex);}
        catch(UnauthorizedAccessException ex){MasterDebugLog.Write("VISUALS",$"Gallery scan denied logicalMod={row.Id}",ex);}
    }


    partial void OnModViewModeChanged(string value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        MasterDebugLog.Write("UI-FILTER",$"Mod smart view changed to {value}");
        ModsView.Refresh();
        OnPropertyChanged(nameof(VisibleModCount));
    }

    partial void OnSearchTextChanged(string value)
    {
        searchCts?.Cancel();
        searchCts?.Dispose();
        var cts=searchCts=new CancellationTokenSource();
        _=DebounceSearchAsync(cts.Token);
    }

    private async Task DebounceSearchAsync(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try
        {
            await Task.Delay(160,ct);
            await Application.Current.Dispatcher.InvokeAsync(()=>
            {
                ModsView.Refresh();
                OnPropertyChanged(nameof(VisibleModCount));
            });
        }
        catch(OperationCanceledException){}
    }

    private bool FilterMod(object o)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(o is not ModRowViewModel m)return false;
        var modeMatch=ModViewMode switch
        {
            "Enabled"=>m.StagedEnabled!=false,
            "Staged"=>m.HasStagedChanges,
            "Updates"=>m.HasUpdate,
            "Issues"=>m.HasIssue,
            "Revalidate"=>m.EffectiveState==EffectiveModState.NeedsRevalidation,
            "Superseded"=>m.EffectiveState==EffectiveModState.FullySuperseded,
            _=>true
        };
        if(!modeMatch)return false;
        if(string.IsNullOrWhiteSpace(SearchText))return true;
        var q=SearchText.Trim();
        return m.DisplayName.Contains(q,StringComparison.OrdinalIgnoreCase)||
               m.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||
               m.PartsLabel.Contains(q,StringComparison.OrdinalIgnoreCase)||
               m.Members.Any(x=>x.DisplayName.Contains(q,StringComparison.OrdinalIgnoreCase)||x.Name.Contains(q,StringComparison.OrdinalIgnoreCase)||x.SourcePath.Contains(q,StringComparison.OrdinalIgnoreCase))||
               (m.Category?.Contains(q,StringComparison.OrdinalIgnoreCase)??false)||
               (m.IssueBadge?.Contains(q,StringComparison.OrdinalIgnoreCase)??false)||
               (m.IssueReason?.Contains(q,StringComparison.OrdinalIgnoreCase)??false);
    }

    private void Changed()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(suppressChanged)return;
        OnPropertyChanged(nameof(EnabledCount));
        OnPropertyChanged(nameof(StagedCount));
        OnPropertyChanged(nameof(StagedEnableCount));
        OnPropertyChanged(nameof(StagedDisableCount));
        OnPropertyChanged(nameof(StagedSummary));
        OnPropertyChanged(nameof(UpdateCount));
        OnPropertyChanged(nameof(AllViewLabel));
        OnPropertyChanged(nameof(EnabledViewLabel));
        OnPropertyChanged(nameof(StagedViewLabel));
        OnPropertyChanged(nameof(UpdatesViewLabel));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(InstalledCountLabel));
        OnPropertyChanged(nameof(IssuesViewLabel));
        OnPropertyChanged(nameof(RevalidateViewLabel));
        OnPropertyChanged(nameof(SupersededViewLabel));
        OnPropertyChanged(nameof(HeaderSummary));
        if(!StringComparer.OrdinalIgnoreCase.Equals(ModViewMode,"All")||!string.IsNullOrWhiteSpace(SearchText))ModsView.Refresh();
        OnPropertyChanged(nameof(VisibleModCount));
        FooterText=StagedCount==0?"No pending changes":$"{StagedCount} pending change(s) — game files stay unchanged until you apply them";
    }

    private async Task ReloadMods(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var loaded=await Task.Run(async ()=>
        {
            var models=await s.Database.GetModsAsync(ct);
            var files=await s.Database.GetModFilesAsync(ct);
            var updateSettings=await s.Database.GetSettingsByPrefixAsync("update:",ct);
            return (models,files,updateSettings);
        },ct);
        var (models,files,updateSettings)=loaded;
        await Application.Current.Dispatcher.InvokeAsync(()=>
        {
            var logical=LogicalModFamilies.Build(models,files,s.Paths.Game);
            var rows=logical.Select(m=>new ModRowViewModel(m,Changed)).ToArray();
            foreach(var row in rows)
            {
                var latest=row.Members.Select(m=>updateSettings.TryGetValue("update:"+m.Id,out var value)?value:null).FirstOrDefault(v=>!string.IsNullOrWhiteSpace(v));
                if(latest is not null&&DateTimeOffset.TryParse(latest,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var when))
                    row.SetUpdateBadge($"Update available • {when:yyyy-MM-dd}");
            }
            Mods.ReplaceAll(rows);
            ModsView.Refresh();
            Changed();
        });
        await RefreshIssueSuspects(ct);
    }

    private Dictionary<string,(bool enabled,int priority)> CaptureStage()=>
        Mods.SelectMany(x=>x.ExpandStage()).ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);

    private async Task<DependencyStatus[]> GetDependencyBlockersAsync(
        Dictionary<string,(bool enabled,int priority)> stage,
        DeploymentPlan? effectivePlan,
        CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var enabled = stage.Where(x => x.Value.enabled).Select(x => x.Key).ToHashSet(PathRules.Comparer);
        var statuses = await s.Dependencies.ScanStageAsync(enabled, effectivePlan, ct);
        return statuses.Where(x => !x.Ready)
            .OrderBy(x => x.ModName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ModId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string DescribeDependencyBlockers(DependencyStatus[] blockers)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var details = blockers.Take(3).Select(x =>
        {
            var missing = x.Missing.Take(3).ToArray();
            var suffix = x.Missing.Count > missing.Length ? $" (+{x.Missing.Count-missing.Length} more)" : string.Empty;
            return $"{x.ModName}: {string.Join(", ", missing)}{suffix}";
        });
        var summary = string.Join(" | ", details);
        if (blockers.Length > 3) summary += $" | +{blockers.Length-3} more mod(s)";
        return summary;
    }

    private async Task<(DeploymentPlan plan,ConflictRow[] rows,List<EffectiveModSummary> summaries)> BuildAnalysisAsync(
        Dictionary<string,(bool enabled,int priority)> stage,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var logicalRows=Mods.Select(row=>new LogicalRowIdentity(
            row.Id,row.DisplayName,row.StagedEnabled!=false,
            row.Members.Any(m=>m.NeedsRevalidation),row.Members.Select(m=>m.PreviewPath).FirstOrDefault(p=>!string.IsNullOrWhiteSpace(p)))).ToArray();
        var logicalByMember=Mods
            .SelectMany(row=>row.Members.Select(member=>new LogicalMemberIdentity(member.Id,row.Id,row.DisplayName,member.PreviewPath)))
            .ToDictionary(x=>x.MemberId,StringComparer.OrdinalIgnoreCase);
        var logicalMemberIds=Mods.ToDictionary(row=>row.Id,row=>(IReadOnlyList<string>)row.Members.Select(m=>m.Id).ToArray(),StringComparer.OrdinalIgnoreCase);
        var enabledIds=stage.Where(x=>x.Value.enabled).Select(x=>x.Key).ToArray();
        return await Task.Run(async () =>
        {
            var snap=await s.PlannerSnapshots.LoadAsync(enabledIds,ct);
            ct.ThrowIfCancellationRequested();
            var staged=snap.Mods.Select(m=>stage.TryGetValue(m.Id,out var v)?m with{Enabled=v.enabled,Priority=v.priority}:m).ToArray();
            var stagedSnap=snap with{Mods=staged};
            var plan=s.Planner.Build(stagedSnap);
            var enabledById=staged.Where(m=>m.Enabled).ToDictionary(m=>m.Id,StringComparer.OrdinalIgnoreCase);
            var filesByPath=new Dictionary<string,List<ModDescriptor>>(StringComparer.OrdinalIgnoreCase);
            foreach(var f in snap.Files)
            {
                if(!enabledById.TryGetValue(f.ModId,out var mod))continue;
                if(!filesByPath.TryGetValue(f.Path,out var list))filesByPath[f.Path]=list=[];
                if(!list.Any(x=>PathRules.Comparer.Equals(x.Id,mod.Id)))list.Add(mod);
            }

            var choices=new Dictionary<string,ChoiceAccumulator>(StringComparer.OrdinalIgnoreCase);
            foreach(var conflict in plan.Conflicts.Where(x=>x.Blocking&&x.Path!="<rules>"))
            {
                List<ModDescriptor>? providers;
                IReadOnlyList<string> conflictPaths=[conflict.Path];
                if(conflict.ReasonCode=="bundle-mixed-providers"&&s.Paths.Game.IsMonsterHunterWorld)
                {
                    var bundleKey=AssetBundles.KeyForPath(conflict.Path);
                    var bundleFiles=snap.Files
                        .Where(file=>enabledById.ContainsKey(file.ModId)&&
                                     file.FileClass==FileClass.Structural&&
                                     StringComparer.OrdinalIgnoreCase.Equals(AssetBundles.KeyForPath(file.Path),bundleKey))
                        .ToArray();
                    conflictPaths=bundleFiles.Select(file=>file.Path).Distinct(PathRules.Comparer).Order(StringComparer.OrdinalIgnoreCase).ToArray();
                    providers=bundleFiles.Select(file=>enabledById[file.ModId])
                        .GroupBy(mod=>mod.Id,PathRules.Comparer)
                        .Select(group=>group.First())
                        .ToList();
                }
                else
                {
                    filesByPath.TryGetValue(conflict.Path,out providers);
                }
                if(providers is null||providers.Count<2)continue;
                var logicalGroups=providers
                    .Select(provider=>logicalByMember.TryGetValue(provider.Id,out var identity)
                        ?(provider:provider,identity:identity)
                        :(provider:provider,identity:new LogicalMemberIdentity(provider.Id,"member:"+provider.Id,provider.DisplayName,provider.PreviewPath)))
                    .GroupBy(x=>x.identity.LogicalId,StringComparer.OrdinalIgnoreCase)
                    .Select(g=>new
                    {
                        LogicalId=g.Key,
                        LogicalName=g.First().identity.LogicalName,
                        Preview=g.Select(x=>x.identity.PreviewPath).FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x)),
                        Members=g.Select(x=>x.provider).ToArray()
                    })
                    .OrderBy(x=>x.LogicalId,StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var options=new List<ConflictOptionRow>();
                if(logicalGroups.Length>=2)
                {
                    foreach(var g in logicalGroups)
                    {
                        var memberIds=logicalMemberIds.TryGetValue(g.LogicalId,out var allIds)?allIds:g.Members.Select(x=>x.Id).ToArray();
                        options.Add(new(g.LogicalId,g.LogicalName,g.Preview,$"{memberIds.Count} source package(s) in this logical mod",memberIds));
                    }
                }
                else
                {
                    var logicalName=logicalGroups[0].LogicalName;
                    foreach(var member in providers.OrderBy(x=>x.Id,StringComparer.OrdinalIgnoreCase))
                        options.Add(new("member:"+member.Id,$"{logicalName} • {member.DisplayName}",member.PreviewPath,"Mutually exclusive component inside one logical family",new[]{member.Id}));
                }

                var bundle=s.Paths.Game.IsMonsterHunterWorld?AssetBundles.KeyForPath(conflict.Path):"file:"+PathRules.Normalize(conflict.Path).ToLowerInvariant();
                // Independent logical alternatives within the same atomic MHW asset bundle collapse
                // into one one-of-N choice. Internal mutually-exclusive members stay scoped to their
                // own logical family so an external choice cannot accidentally re-enable all parts.
                var key=logicalGroups.Length>=2?bundle:$"{bundle}|internal:{logicalGroups[0].LogicalId}";
                if(!choices.TryGetValue(key,out var acc))choices[key]=acc=new ChoiceAccumulator(bundle);
                acc.Paths.UnionWith(conflictPaths);
                acc.Conflicts.Add(conflict);
                foreach(var option in options)acc.Options[option.Token]=option;
            }

            var rows=choices.Values.Select(acc=>
            {
                var paths=acc.Paths.Order(StringComparer.OrdinalIgnoreCase).ToArray();
                var options=acc.Options.Values.OrderBy(x=>x.DisplayName,StringComparer.OrdinalIgnoreCase).ToArray();
                var first=acc.Conflicts[0];
                var score=Math.Max(95,acc.Conflicts.Max(x=>x.ResolverScore));
                var evidence=string.Join(" • ",acc.Conflicts.Select(x=>x.Evidence).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
                if(string.IsNullOrWhiteSpace(evidence))evidence=s.Paths.Game.IsMonsterHunterWorld?"Independent logical mods provide different bytes inside the same atomic MHW asset bundle.":"Independent logical mods provide different bytes for the same game path.";
                var explanation=first.ReasonCode=="bundle-mixed-providers"
                    ?first.Explanation+" Choose one coherent provider/family for the entire bundle; the manager will not mix structural siblings."
                    :options.Length==2
                        ?"These two logical mods provide different bytes for the same effective game asset/path. Choose one; the other logical mod is staged OFF as a whole."
                        :$"{options.Length} logical mods provide different bytes for the same effective game asset/path. Choose one winner; every other alternative is staged OFF as a whole.";
                return new ConflictRow(acc.BundleKey,paths[0],paths,paths.Length,first.Kind,
                    explanation,
                    Confidence.High,score,evidence,options);
            }).OrderBy(x=>x.Scope,StringComparer.OrdinalIgnoreCase).ToArray();

            var totalPaths=logicalRows.ToDictionary(x=>x.Id,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            var winningPaths=logicalRows.ToDictionary(x=>x.Id,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            var shadowedPaths=logicalRows.ToDictionary(x=>x.Id,_=>new HashSet<string>(StringComparer.OrdinalIgnoreCase),StringComparer.OrdinalIgnoreCase);
            foreach(var f in snap.Files)
            {
                if(!stage.TryGetValue(f.ModId,out var state)||!state.enabled)continue;
                if(logicalByMember.TryGetValue(f.ModId,out var identity)&&totalPaths.TryGetValue(identity.LogicalId,out var set))set.Add(f.Path);
            }

            // Count the actual effective provider for every staged path, including paths with no
            // conflict at all. This is the effective-mod view, not merely a conflict counter.
            var decisionByPath=plan.Conflicts.ToDictionary(d=>d.Path,StringComparer.OrdinalIgnoreCase);
            foreach(var (path,providers) in filesByPath)
            {
                var logicalProviders=providers
                    .Select(p=>logicalByMember.TryGetValue(p.Id,out var id)?id.LogicalId:null)
                    .Where(id=>id is not null)
                    .Cast<string>()
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if(logicalProviders.Length==0)continue;

                string? winnerLogical=null;
                if(logicalProviders.Length==1)winnerLogical=logicalProviders[0];
                else if(decisionByPath.TryGetValue(path,out var decision)&&!decision.Blocking&&decision.WinnerModId is not null&&logicalByMember.TryGetValue(decision.WinnerModId,out var winnerIdentity))winnerLogical=winnerIdentity.LogicalId;

                if(winnerLogical is null)continue;
                if(winningPaths.TryGetValue(winnerLogical,out var wins))wins.Add(path);
                foreach(var losingLogical in logicalProviders.Where(id=>!StringComparer.OrdinalIgnoreCase.Equals(id,winnerLogical)))
                    if(shadowedPaths.TryGetValue(losingLogical,out var shadows))shadows.Add(path);
            }
            var blockedLogical=rows.SelectMany(r=>r.Options).Select(o=>o.Token)
                .Select(token=>token.StartsWith("member:",StringComparison.OrdinalIgnoreCase)
                    ?(logicalByMember.TryGetValue(token["member:".Length..],out var id)?id.LogicalId:null)
                    :token)
                .Where(x=>x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var summaries=new List<EffectiveModSummary>(logicalRows.Length);
            foreach(var row in logicalRows)
            {
                var total=totalPaths[row.Id].Count;var wins=winningPaths[row.Id].Count;var shadows=shadowedPaths[row.Id].Count;
                EffectiveModState state;string reason;
                if(!row.Enabled){state=EffectiveModState.Disabled;reason="This mod is turned off in the pending setup.";}
                else if(row.NeedsRevalidation){state=EffectiveModState.NeedsRevalidation;reason="The game was updated or changed, so this mod should be checked again before use.";}
                else if(blockedLogical.Contains(row.Id)){state=EffectiveModState.NeedsChoice;reason="Another unrelated mod changes the same game content. Choose which one should win.";}
                else if(total>0&&wins==0){state=EffectiveModState.FullySuperseded;reason="Another enabled mod currently provides every game file from this mod.";}
                else if(shadows>0){state=EffectiveModState.PartiallyOverridden;reason=$"{shadows} file(s) are replaced by another enabled add-on or update; the rest are still used.";}
                else{state=EffectiveModState.FullyEffective;reason="All files from this mod will be used in the current setup.";}
                summaries.Add(new(row.Id,state,wins,shadows,total,reason));
            }
            return (plan,rows,summaries);
        },ct);
    }

    private async Task RefreshAnalysis(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var generation=unchecked(++conflictPreviewGeneration);
        var stage=CaptureStage();
        var analysis=await BuildAnalysisAsync(stage,ct);
        var shouldEnrich=SelectedTab==3;
        var displayRows=shouldEnrich?await EnrichConflictPreviewsAsync(analysis.rows,ct):analysis.rows;
        await Application.Current.Dispatcher.InvokeAsync(()=>
        {
            Conflicts.ReplaceAll(displayRows);
            conflictPreviewLoadedGeneration=shouldEnrich?generation:-1;
            var summaryById=analysis.summaries.ToDictionary(x=>x.LogicalModId,StringComparer.OrdinalIgnoreCase);
            foreach(var row in Mods)if(summaryById.TryGetValue(row.Id,out var summary))row.SetEffective(summary);
            ModsView.Refresh();
            OnPropertyChanged(nameof(FullyEffectiveCount));
            OnPropertyChanged(nameof(ComposedCount));
            OnPropertyChanged(nameof(RevalidationCount));
            OnPropertyChanged(nameof(SupersededCount));
            OnPropertyChanged(nameof(VisibleModCount));
            OnPropertyChanged(nameof(RevalidateViewLabel));
            OnPropertyChanged(nameof(SupersededViewLabel));
            OnPropertyChanged(nameof(HeaderSummary));
        });
        overlapsLoaded=false;
        if(SelectedTab==6)await EnsureDeferredPageLoadedAsync(6,ct);
    }

    private async Task<ConflictRow[]> EnrichConflictPreviewsAsync(ConflictRow[] rows,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(rows.Length==0)return rows;
        var modsById=Mods.SelectMany(r=>r.Members).ToDictionary(m=>m.Id,StringComparer.OrdinalIgnoreCase);
        var logicalById=Mods.ToDictionary(r=>r.Id,StringComparer.OrdinalIgnoreCase);
        var output=new List<ConflictRow>(rows.Length);
        foreach(var row in rows)
        {
            var options=new List<ConflictOptionRow>(row.Options.Count);
            foreach(var option in row.Options)
            {
                if(!string.IsNullOrWhiteSpace(option.PreviewPath)){options.Add(option);continue;}
                ModDescriptor? mod=null;
                if(option.Token.StartsWith("member:",StringComparison.OrdinalIgnoreCase))modsById.TryGetValue(option.Token["member:".Length..],out mod);
                else if(logicalById.TryGetValue(option.Token,out var logical))
                {
                    for(var i=0;i<logical.Members.Count;i++)
                    {
                        if(!logical.Members[i].Enabled)continue;
                        mod=logical.Members[i];
                        break;
                    }
                    if(mod is null&&logical.Members.Count>0)mod=logical.Members[0];
                }
                var preview=mod is null?null:await s.TexturePreviews.GetPreviewAsync(mod,row.Path,ct);
                options.Add(option with{PreviewPath=preview});
            }
            output.Add(row with{Options=options});
        }
        return output.ToArray();
    }

    [RelayCommand]
    private void SetModView(string? mode)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mode={mode}");
        ModViewMode=string.IsNullOrWhiteSpace(mode)?"All":mode;
        SelectedTab=1;
    }

    [RelayCommand]
    private void ClearModFilters()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        SearchText=string.Empty;
        ModViewMode="All";
        SelectedTab=1;
    }

    [RelayCommand]
    private void DiscardStaged()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(StagedCount==0){StatusText="There are no pending changes to discard.";return;}
        if(ConfirmBeforeDiscardStaged&&MessageBox.Show(
            $"Discard {StagedCount} pending mod change(s)? Your installed game files will not be changed.",
            "Discard pending changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question)!=MessageBoxResult.Yes)
        {
            StatusText="Discard cancelled. Pending changes were kept.";
            return;
        }
        suppressChanged=true;
        try{foreach(var row in Mods)row.DiscardStaged();}
        finally{suppressChanged=false;Changed();}
        PlanPreviewText="No change preview yet.";
        StatusText="Discarded all pending changes. Your installed game files were not changed.";
    }

    [RelayCommand]
    private async Task PreviewApply()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("deployment.preview","Previewing Mod Changes","Checking exactly what will change without editing game files…",true,async ct=>
    {
        var stage=CaptureStage();
        var current=await s.Database.GetModsAsync(ct);
        var enabling=current.Where(m=>stage.TryGetValue(m.Id,out var v)&&v.enabled&&!m.Enabled).Select(m=>m with{Enabled=true}).ToArray();
        if(enabling.Length>0)await s.Catalog.EnsureCapturedAsync(enabling,ct);
        var analysis=await BuildAnalysisAsync(stage,ct);
        var changes=analysis.plan.Changes;
        var add=changes.Count(x=>x.Kind==ChangeKind.Add);
        var replace=changes.Count(x=>x.Kind==ChangeKind.Replace);
        var remove=changes.Count(x=>x.Kind==ChangeKind.Remove);
        var restore=changes.Count(x=>x.Kind==ChangeKind.RestoreOriginal);
        if(analysis.plan.IsBlocked)
        {
            var displayRows=await EnrichConflictPreviewsAsync(analysis.rows,ct);
            await Application.Current.Dispatcher.InvokeAsync(()=>Conflicts.ReplaceAll(displayRows));
            var blockingCount=analysis.plan.Conflicts.Count(x=>x.Blocking);
            PlanPreviewText=$"Blocked • {Math.Max(analysis.rows.Length,blockingCount)} blocking decision(s) • no files would be written";
            StatusText=analysis.rows.Length>0?PlanPreviewText:PlanPreviewText+" Check the rule/dependency diagnostics for the exact reason.";
            SelectedTab=3;
            return;
        }
        var dependencyBlockers=await GetDependencyBlockersAsync(stage,analysis.plan,ct);
        if(dependencyBlockers.Length>0)
        {
            PlanPreviewText=$"Blocked • {dependencyBlockers.Length} mod(s) have unsatisfied effective requirements • no files would be written";
            StatusText="Dependency validation blocked deployment. "+DescribeDependencyBlockers(dependencyBlockers);
            SelectedTab=1;
            return;
        }
        PlanPreviewText=$"Ready • {changes.Count} file change(s): {add} add • {replace} replace • {remove} remove • {restore} restore";
        StatusText="Dry run passed. "+PlanPreviewText+".";
        SelectedTab=0;
    });
    }

    [RelayCommand]
    private async Task RefreshAnalysisNow()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("analysis.refresh","Refreshing Mod Status","Checking which mods and files will be used and whether anything needs your attention…",true,RefreshAnalysis);
    }

    [RelayCommand]
    private async Task Apply()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(ConfirmBeforeApply&&StagedCount>0&&MessageBox.Show(
            $"Apply {StagedCount} pending mod change(s) to {s.Paths.Game.DisplayName}?",
            "Apply mod changes",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question)!=MessageBoxResult.Yes)
        {
            StatusText="Apply cancelled. No game files were changed.";
            return;
        }
        await RunBusy("deployment.apply","Applying Mod Changes","Checking the setup, creating recovery information, and safely updating the game files…",false,async ct=>
    {
        var blockers=s.ProcessGuard.GetKnownBlockers();
        if(blockers.Count>0)throw new InvalidOperationException($"{s.Paths.Game.DisplayName} is running. Close it before deployment.");

        var stage=CaptureStage();
        var current=await s.Database.GetModsAsync(ct);
        var enabling=current.Where(m=>stage.TryGetValue(m.Id,out var v)&&v.enabled&&!m.Enabled).Select(m=>m with{Enabled=true}).ToArray();
        await s.Catalog.EnsureCapturedAsync(enabling,ct);

        var analysis=await BuildAnalysisAsync(stage,ct);
        var displayRows=analysis.plan.IsBlocked?await EnrichConflictPreviewsAsync(analysis.rows,ct):analysis.rows;
        await Application.Current.Dispatcher.InvokeAsync(()=>Conflicts.ReplaceAll(displayRows));
        if(analysis.plan.IsBlocked)
        {
            SelectedTab=3;
            var blockingCount=analysis.plan.Conflicts.Count(x=>x.Blocking);
            StatusText=analysis.rows.Length>0
                ?$"{analysis.rows.Length} compacted conflict choice(s) need attention. Nothing was written."
                :$"{blockingCount} resolver/rule safety blocker(s) need attention. Nothing was written.";
            return;
        }

        var dependencyBlockers=await GetDependencyBlockersAsync(stage,analysis.plan,ct);
        if(dependencyBlockers.Length>0)
        {
            StatusText="Dependency preflight blocked Apply. Nothing was written. "+DescribeDependencyBlockers(dependencyBlockers);
            SelectedTab=1;
            return;
        }

        var audits=analysis.plan.Conflicts.Where(d=>d.Inferred&&d.ResolverScore>0)
            .Select(d=>new ResolverAudit(d.Path,d.WinnerModId,d.ResolverScore,d.ReasonCode,d.Explanation,d.Evidence??string.Empty)).ToArray();
        if(audits.Length>0)await s.Database.RecordResolverAuditAsync(audits,ct);

        var result=await s.Executor.ApplyAsync(analysis.plan,$"Apply {StagedCount} staged mod-state change(s)",stage,ct:ct);
        if(!result.Success)
            throw new InvalidOperationException($"{result.Message} Rollback completed: {result.RollbackCompleted}.",result.Exception);

        var enabledAfter=stage.Where(x=>x.Value.enabled).Select(x=>x.Key).ToArray();
        if(enabledAfter.Length>0)await s.Database.MarkRevalidationAsync(enabledAfter,"Validated by a successful deployment after the current game build was observed.",false,ct);
        await Application.Current.Dispatcher.InvokeAsync(()=>{foreach(var row in Mods)row.CommitApplied();});
        StatusText=result.Message;
        await RefreshAnalysis(ct);
        await RefreshActivity(ct);
        Changed();
    });
    }

    [RelayCommand]
    private async Task AutoPopulate()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var preferredModIds=Mods
            .SelectMany(row=>row.ExpandStage().Where(x=>x.Value.enabled).Select(x=>x.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        AutoPopulateResult? result=null;
        await RunBusy(
            "mods.auto-populate",
            "Auto Populating Mods",
            "Keeping your currently selected mods, then filling around them with compatible mods, dependencies, and required texture providers…",
            true,
            async ct=>
            {
                var service=new AutoPopulateService(s.PlannerSnapshots,s.Planner,s.Dependencies,s.Paths.GameRoot,s.Paths.Game);
                result=await service.BuildAsync(preferredModIds,ct);
                var stage=result.State.ToDictionary(
                    x=>x.Key,
                    x=>(x.Value.Enabled,x.Value.Priority),
                    StringComparer.OrdinalIgnoreCase);

                await Application.Current.Dispatcher.InvokeAsync(()=>
                {
                    suppressChanged=true;
                    try{foreach(var row in Mods)row.ApplyProfileState(stage);}
                    finally{suppressChanged=false;Changed();}
                });

                await RefreshAnalysis(ct);
                StatusText=result.Summary+" Applying the safe setup now…";
                await s.Timeline.RecordAsync(
                    "mods.auto-populate",
                    AutomationSeverity.Info,
                    result.Summary,
                    new{result.EnabledMods,result.SkippedConflicts,result.SkippedRequirements},
                    ct);
            });

        if(result is null)return;
        if(BlockerCount>0)
        {
            StatusText="Auto Populate stopped because the final setup still needs a conflict choice. Nothing was applied.";
            SelectedTab=3;
            return;
        }

        if(StagedCount>0)await Apply();
        if(StagedCount==0&&BlockerCount==0)
            StatusText=result.Summary+" The setup is applied and ready to launch.";
    }

    [RelayCommand]private void EnableSelected()=>StageVisible(true);
    [RelayCommand]private void DisableSelected()=>StageVisible(false);

    private void StageVisible(bool enabled)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        suppressChanged=true;
        try{foreach(var m in ModsView.Cast<ModRowViewModel>().ToArray())m.StagedEnabled=enabled;}
        finally{suppressChanged=false;Changed();}
    }

    [RelayCommand]
    private async Task ReindexSelected()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("catalog.reindex","Refreshing Mod Info","Re-reading the installed mod packages without changing game files…",true,async ct=>
    {
        var target=ModsView.Cast<ModRowViewModel>()
            .SelectMany(x=>x.StagedMemberDescriptors())
            .DistinctBy(x=>x.Id,StringComparer.OrdinalIgnoreCase)
            .ToArray();
        await s.Catalog.EnsureCapturedAsync(target,ct);
        StatusText=$"Re-indexed {target.Length} visible enabled mod(s).";
    });
    }

    [RelayCommand]
    private async Task SyncMetadata()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("metadata.sync","Checking Mod Information","Reading available mod metadata and update information, then refreshing known relationships…",true,async ct=>
    {
        await metadataGate.WaitAsync(ct);
        try
        {
            var result=await s.Nexus.RefreshAsync(true,ct);
            await ReloadMods(ct);await RefreshAnalysis(ct);
            var visualTotal=result.LocalVisuals+result.DeclaredVisuals+result.PublicVisuals+result.VisualsRefreshed;
            StatusText=result.ApiEnabled
                ?$"Metadata refreshed: {result.ApiRecords} Nexus record(s), {visualTotal} visual(s) discovered/refreshed ({result.LocalVisuals} local, {result.DeclaredVisuals} Vortex/sidecar, {result.PublicVisuals} public Nexus, {result.VisualsRefreshed} API), {result.UpdatesAvailable} update(s) available."
                :$"Visual sync: {visualTotal} visual(s) discovered/refreshed ({result.LocalVisuals} local, {result.DeclaredVisuals} Vortex/sidecar, {result.PublicVisuals} public Nexus). API key is optional for basic artwork; add one only for richer Nexus metadata and update checks.";
        }
        finally{metadataGate.Release();}
    });
    }

    [RelayCommand]
    private async Task AdoptManualFiles()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("catalog.adopt","Bringing In Manual Mods","Copying existing manual mod files into the library without deleting or changing the files currently used by the game…",true,async ct=>
    {
        if(!SupportsLiveAdoption){StatusText="This game setup does not support importing already-installed manual mod files.";return;}
        var result=await s.Adoption.AdoptAsync(ct);
        if(!result.Created){StatusText=result.Message;return;}
        await s.Catalog.RefreshFoldersAsync(ct);
        await metadataGate.WaitAsync(ct);
        try{await s.Nexus.RefreshAsync(ct);}
        finally{metadataGate.Release();}
        await ReloadMods(ct);UnmanagedFileCount=SupportsLiveAdoption?await s.Adoption.CountAsync(ct):0;await RefreshAnalysis(ct);
        StatusText=result.Message+" The imported mod stays disabled until you enable it and apply your changes.";
    });
    }

    [RelayCommand]private void CancelBusy()=>busyCts?.Cancel();

    [RelayCommand]
    private async Task Health()=>await RunBusy("health.scan","Health check","Verifying database, blobs, managed live files, and rule invariants…",true,async ct=>
    {
        var issues=await s.Health.ScanAsync(ct);
        StatusText=issues.Count==0?"Health check passed.":$"Health check found {issues.Count} issue(s). Export a support bundle for details.";
    });

    [RelayCommand]
    private void CaptureDiagnostics()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var script=Path.Combine(s.Paths.ToolRoot,"scripts","Capture-Diagnostics.ps1");
        if(!File.Exists(script)){StatusText="Capture-Diagnostics.ps1 is not present in this installation.";return;}
        try
        {
            using var diagnosticProcess = ProcessDebug.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -ProcessId {Environment.ProcessId}")
            {
                WorkingDirectory=s.Paths.ToolRoot,UseShellExecute=true
            }, "capture-diagnostics");
            StatusText="Diagnostic capture started in a separate PowerShell window. Keep the manager running while it records the freeze/load.";
        }
        catch(Exception ex){StatusText="Could not start diagnostic capture: "+ex.Message;}
    }

    [RelayCommand]
    private async Task ExportSupport()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("diagnostics.bundle","Creating Support Bundle","Collecting recent logs and small diagnostic details that can help troubleshoot a problem…",true,async ct=>
    {
        var path=await s.Support.CreateAsync(Path.Combine(s.Paths.ToolRoot,"Support Bundles"),ct);
        StatusText=$"Support bundle: {path}";
    });
    }

    [RelayCommand]
    private async Task StageProfile()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(SelectedProfile is null){StatusText="Select a profile first.";return;}
        await RunBusy("profile.stage","Preparing Profile","Loading this profile as pending changes without editing game files…",true,async ct=>
        {
            var state=await s.Profiles.LoadAsync(SelectedProfile.Id,ct);
            await Application.Current.Dispatcher.InvokeAsync(()=>
            {
                suppressChanged=true;
                try
                {
                    foreach(var row in Mods)row.ApplyProfileState(state);
                }
                finally{suppressChanged=false;Changed();}
            });
            StatusText=$"Profile '{SelectedProfile.Name}' is ready to use. Game files stay unchanged until you apply the changes.";
        });
    }

    [RelayCommand]
    private async Task SaveProfile()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(string.IsNullOrWhiteSpace(NewProfileName)){StatusText="Enter a profile name first.";return;}
        await RunBusy("profile.save","Saving Profile","Saving the current enabled mods and load order…",true,async ct=>
        {
            await s.Profiles.SaveCurrentAsync(NewProfileName.Trim(),ct);
            await RefreshProfiles(ct);
            StatusText=$"Saved profile '{NewProfileName.Trim()}'.";
        });
    }

    private async Task RefreshIssueSuspects(CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var suspects=await Task.Run(()=>s.Issues.GetActiveAsync(ct),ct);
        static string IssueKindLabel(ModIssueKind kind)=>kind switch
        {
            ModIssueKind.GpuGraphicsCrash=>"GPU / graphics crash",
            ModIssueKind.StartupCrash=>"Startup crash",
            ModIssueKind.BisectIsolated=>"Crash bisector isolated",
            _=>"Game crash"
        };
        var rows=suspects.Select(x=>new ModIssueRow(x.ModId,x.DisplayName,IssueKindLabel(x.Kind),x.Score,x.Reason,x.LastSeen.LocalDateTime.ToString("g",CultureInfo.CurrentCulture),x.FailureCount,x.Confirmed)).ToArray();
        await Application.Current.Dispatcher.InvokeAsync(()=>
        {
            IssueSuspects.ReplaceAll(rows);
            var byMember=suspects.GroupBy(x=>x.ModId,StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.OrderByDescending(x=>x.Confirmed).ThenByDescending(x=>x.Score).First(),StringComparer.OrdinalIgnoreCase);
            foreach(var mod in Mods)
            {
                var matches=mod.Members.Select(m=>byMember.TryGetValue(m.Id,out var issue)?issue:null).Where(x=>x is not null).Cast<ModIssueSuspect>().OrderByDescending(x=>x.Confirmed).ThenByDescending(x=>x.Score).ToArray();
                if(matches.Length==0)mod.SetIssue(null,0,null);
                else
                {
                    var top=matches[0];
                    mod.SetIssue(top.Confirmed?$"⚠ ISOLATED • {IssueKindLabel(top.Kind)}":$"⚠ {top.Score}% • {IssueKindLabel(top.Kind)}",top.Score,top.Reason);
                }
            }
            ModsView.Refresh();
            OnPropertyChanged(nameof(IssueCount));
            OnPropertyChanged(nameof(IssuesViewLabel));
            OnPropertyChanged(nameof(VisibleModCount));
            OnPropertyChanged(nameof(HeaderSummary));
        });
    }

    [RelayCommand]
    private async Task ReportGpuGraphicsCrash()=>await RunBusy("diagnosis.gpu-report","Reporting GPU/graphics crash","Comparing the last modded launch with the previous successful launch and ranking suspect mods…",true,async ct=>
    {
        var result=await s.Issues.AnalyzeLatestLaunchAsync(ModIssueKind.GpuGraphicsCrash,ct);
        await RefreshIssueSuspects(ct);
        SelectedTab=3;
        StatusText=result.Message;
    });

    [RelayCommand]
    private async Task ReportGameCrash()=>await RunBusy("diagnosis.game-report","Reporting game crash","Comparing the last modded launch with the previous successful launch and ranking suspect mods…",true,async ct=>
    {
        var result=await s.Issues.AnalyzeLatestLaunchAsync(ModIssueKind.GameCrash,ct);
        await RefreshIssueSuspects(ct);
        SelectedTab=3;
        StatusText=result.Message;
    });

    [RelayCommand]
    private async Task ClearIssueSuspect(string? modId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(string.IsNullOrWhiteSpace(modId))return;
        await RunBusy("diagnosis.issue-clear","Clearing Problem Mark","Removing this warning without changing the mod or installed game files…",true,async ct=>
        {
            await s.Issues.ClearAsync(modId,ct);
            await RefreshIssueSuspects(ct);
            StatusText="Cleared the issue mark. No mod files or enabled state were changed.";
        });
    }

    [RelayCommand]
    private async Task Undo()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("deployment.undo","Undo Last Change","Restoring the mod setup from before the last applied change…",false,async ct=>
    {
        var result=await s.Executor.UndoLastAsync(ct);
        if(!result.Success)throw new InvalidOperationException($"{result.Message} Rollback completed: {result.RollbackCompleted}.",result.Exception);
        await ReloadMods(ct);await RefreshAnalysis(ct);await RefreshActivity(ct);StatusText=result.Message;
    });
    }

    [RelayCommand]
    private async Task LaunchModded()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(StagedCount>0)
        {
            await Apply();
            if(StagedCount>0||BlockerCount>0){StatusText="Launch stopped because the pending mod changes could not be applied safely.";return;}
        }
        await RunBusy("game.just-play","Launch Game","Backing up your save, checking the mod setup, and then launching the game…",true,async ct=>
        {
            var dependencyBlockers=await GetDependencyBlockersAsync(CaptureStage(),null,ct);
            if(dependencyBlockers.Length>0)
            {
                StatusText="Launch blocked because enabled mods have unsatisfied requirements. "+DescribeDependencyBlockers(dependencyBlockers);
                return;
            }
            var observation=await s.Automation.LaunchAndObserveAsync(LaunchMode.Modded,TimeSpan.FromSeconds(15),ct);
            StatusText=observation.Message;
            await RefreshActivity(ct);
            await RefreshIssueSuspects(ct);
            if(!observation.StartupSurvived&&observation.Started)SelectedTab=3;
        });
    }

    [RelayCommand]
    private async Task ProcessInbox()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("automation.inbox","Installing Mods from Inbox","Checking each package, importing safe mods, and handling obvious duplicates automatically…",true,async ct=>
    {
        var result=await s.Inbox.ProcessAsync(ct);
        var archived=await s.Duplicates.ArchiveSafeAsync(ct);
        await ReloadMods(ct);await RefreshAnalysis(ct);
        StatusText=$"Inbox finished: {result.Imported} installed, {result.Skipped} skipped, {archived} older or duplicate package(s) archived. You can add ZIP/RAR/7z files or folders to '{s.Paths.InboxRoot}'.";
    });
    }

    [RelayCommand]
    private async Task RestoreLastGood()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("automation.restore-lkg","Restoring Last Working Setup","Restoring the most recent setup that successfully started the game…",false,async ct=>
        {
        var known=await s.LastGood.LoadAsync(ct)??throw new InvalidOperationException("No last-known-good launch exists yet.");
        var stage=known.Mods.ToDictionary(x=>x.Key,x=>(x.Value.Enabled,x.Value.Priority),StringComparer.OrdinalIgnoreCase);
        var snap=await s.PlannerSnapshots.LoadAsync(ct);
        var staged=snap.Mods.Select(m=>stage.TryGetValue(m.Id,out var v)?m with{Enabled=v.Enabled,Priority=v.Priority}:m with{Enabled=false}).ToArray();
        var plan=await Task.Run(()=>s.Planner.Build(snap with{Mods=staged}),ct);
        if(plan.IsBlocked)throw new InvalidOperationException("The saved setup now has a blocking conflict under the current files; nothing was changed.");
        var dependencyBlockers=await GetDependencyBlockersAsync(stage,plan,ct);
        if(dependencyBlockers.Length>0)
            throw new InvalidOperationException("The saved setup no longer satisfies its effective requirements, so it was not restored: "+DescribeDependencyBlockers(dependencyBlockers));
        var result=await s.Executor.ApplyAsync(plan,"Restore last known good",stage,ct:ct);
        if(!result.Success)throw result.Exception??new InvalidOperationException(result.Message);
        await ReloadMods(ct);await RefreshAnalysis(ct);await RefreshActivity(ct);
            StatusText="Restored last known good from "+known.RecordedAt.LocalDateTime.ToString("g",CultureInfo.CurrentCulture)+".";
        });
    }

    [RelayCommand]
    private async Task ExportRecipe()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("automation.recipe","Exporting Mod List","Saving the mod list, enabled state, load order, and source references without copying the mod files themselves…",true,async ct=>
    {
        var dir=Path.Combine(s.Paths.ToolRoot,"Collection Recipes");Directory.CreateDirectory(dir);
        var path=Path.Combine(dir,GameProfile.NormalizeId(s.Paths.Game.DisplayName)+"-collection-"+DateTime.Now.ToString("yyyyMMdd-HHmmss",CultureInfo.InvariantCulture)+".json");
        await s.Recipe.ExportAsync(path,ct);StatusText="Collection recipe exported: "+path;
    });
    }

    [RelayCommand]
    private async Task SmartCleanup()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("automation.cleanup","Cleaning Up Duplicate Mods","Moving only safe disabled duplicates and older replaced packages into the archive. Nothing is permanently deleted…",true,async ct=>
    {
        var analysis=await s.Duplicates.AnalyzeAsync(ct);var moved=await s.Duplicates.ArchiveSafeAsync(ct);
        await s.Catalog.RefreshFoldersAsync(ct);await ReloadMods(ct);
        StatusText=$"Cleanup archived {moved} folder(s). "+(analysis.ReclaimableBytes/1024d/1024d/1024d).ToString("F2",CultureInfo.CurrentCulture)+" GB of duplicate or older replaced data was identified before cleanup.";
    });
    }

    [RelayCommand]
    private async Task AutoDiagnoseCrash()=>await RunBusy("automation.bisect","Automatic crash diagnosis","Bisecting changed mods or persisted issue suspects against a safe baseline. The game may launch several times and surviving probes will be closed automatically…",false,async ct=>
    {
        var known=await s.LastGood.LoadAsync(ct)??throw new InvalidOperationException("No last-known-good launch exists yet. Launch successfully once before using automatic bisect.");
        var current=await s.Database.GetModsAsync(ct);
        var suspects=current.Where(m=>m.Enabled&&(!known.Mods.TryGetValue(m.Id,out var old)||!old.Enabled)).Select(m=>m.Id).ToArray();
        if(suspects.Length==0)
        {
            var marked=await s.Issues.GetActiveAsync(ct);
            var enabled=current.Where(m=>m.Enabled).Select(m=>m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            suspects=marked.Where(x=>enabled.Contains(x.ModId)).OrderByDescending(x=>x.Confirmed).ThenByDescending(x=>x.Score).Select(x=>x.ModId).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
            if(suspects.Length==0)throw new InvalidOperationException("No newly enabled mods or active issue suspects are available to bisect. Report the game/GPU crash first so the fallback can build a suspect set.");
            var baseline=known.Mods.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
            foreach(var id in suspects)
            {
                if(baseline.TryGetValue(id,out var oldState))baseline[id]=oldState with{Enabled=false};
                else if(current.FirstOrDefault(m=>StringComparer.OrdinalIgnoreCase.Equals(m.Id,id)) is { } mod)baseline[id]=new ModState(false,mod.Priority);
            }
            known=known with{Mods=baseline};
            MasterDebugLog.Write("MOD-ISSUE",$"Crash bisector fallback is using {suspects.Length} persisted issue suspect(s) as the removable test set.");
        }
        var currentState=current.ToDictionary(m=>m.Id,m=>(m.Enabled,m.Priority),StringComparer.OrdinalIgnoreCase);
        try
        {
            var result=await s.Bisector.RunAsync(suspects,async (subset,token)=>await ProbeCrashSubsetAsync(known,currentState,subset,token),ct);
            StatusText=result.Isolated?$"Crash bisector isolated: {string.Join(", ",result.Suspects)} after {result.Probes} probe(s).":result.Message;
            await s.Timeline.RecordAsync("diagnosis.bisect",result.Isolated?AutomationSeverity.Warning:AutomationSeverity.Info,StatusText,new{result.Suspects,result.Probes},ct);
            if(result.Isolated)await s.Issues.MarkBisectResultAsync(result.Suspects,ct:ct);
            await RefreshIssueSuspects(ct);
        }
        finally
        {
            await ApplyStateDirectAsync(currentState,"Restore setup after crash diagnosis",ct);
            await ReloadMods(ct);await RefreshAnalysis(ct);
        }
    });

    private async Task<bool> ProbeCrashSubsetAsync(LastKnownGoodState known,Dictionary<string,(bool enabled,int priority)> current,IReadOnlySet<string> subset,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var stage=known.Mods.ToDictionary(x=>x.Key,x=>(x.Value.Enabled,x.Value.Priority),StringComparer.OrdinalIgnoreCase);
        foreach(var id in subset)if(current.TryGetValue(id,out var now))stage[id]=now;
        await ApplyStateDirectAsync(stage,"Crash diagnosis probe",ct);
        var exe=s.Paths.ExecutablePath;
        using var process=ProcessDebug.Start(new ProcessStartInfo(exe){WorkingDirectory=s.Paths.GameRoot,UseShellExecute=true}, "game-crash-probe");
        var delay=Task.Delay(TimeSpan.FromSeconds(12),ct);var exit=process.WaitForExitAsync(ct);var completed=await Task.WhenAny(delay,exit);
        if(completed==exit)return true;
        try{process.Kill(true);await process.WaitForExitAsync(ct);}catch(InvalidOperationException){}
        return false;
    }

    private async Task ApplyStateDirectAsync(Dictionary<string,(bool enabled,int priority)> state,string description,CancellationToken ct)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var snap=await s.PlannerSnapshots.LoadAsync(ct);
        var staged=snap.Mods.Select(m=>state.TryGetValue(m.Id,out var v)?m with{Enabled=v.enabled,Priority=v.priority}:m with{Enabled=false}).ToArray();
        var plan=await Task.Run(()=>s.Planner.Build(snap with{Mods=staged}),ct);
        if(plan.IsBlocked)throw new InvalidOperationException("Automatic diagnosis hit a blocking structural conflict and stopped without guessing.");
        var dependencyBlockers=await GetDependencyBlockersAsync(state,plan,ct);
        if(dependencyBlockers.Length>0)
            throw new InvalidOperationException(description+" blocked by dependency preflight before any game file was changed. "+DescribeDependencyBlockers(dependencyBlockers));
        var result=await s.Executor.ApplyAsync(plan,description,state,ct:ct);
        if(!result.Success)throw result.Exception??new InvalidOperationException(result.Message);
    }

    [RelayCommand]
    private async Task LaunchSafeMode()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunBusy("game.safe-mode","Safe mode",$"Temporarily removing manager-controlled mods, launching {s.Paths.Game.DisplayName}, then restoring your applied configuration after the game exits…",false,async ct=>
        {
        if(s.ProcessGuard.GetKnownBlockers().Count>0)throw new InvalidOperationException($"{s.Paths.Game.DisplayName} is already running.");
        await s.Backups.CreateAsync("pre-vanilla-launch",ct);
        var snap=await s.PlannerSnapshots.LoadAsync(ct);
        var none=snap.Mods.Select(m=>m with{Enabled=false}).ToArray();
        var offPlan=await Task.Run(()=>s.Planner.Build(snap with{Mods=none}),ct);
        var off=await s.Executor.ApplyAsync(offPlan,"Enter safe mode",ct:ct);
        if(!off.Success)throw off.Exception??new InvalidOperationException(off.Message);
        using var p=ProcessDebug.Start(new ProcessStartInfo(s.Paths.ExecutablePath){WorkingDirectory=s.Paths.GameRoot,UseShellExecute=true}, "game-safe-mode-launch");
        await p.WaitForExitAsync(ct);
        var restoreSnap=await s.PlannerSnapshots.LoadAsync(ct);
        var restore=await Task.Run(()=>s.Planner.Build(restoreSnap),ct);
        if(restore.IsBlocked)throw new InvalidOperationException("The previous mod configuration now has a blocking conflict; safe-mode restore stopped without guessing.");
        var restoreState=restoreSnap.Mods.ToDictionary(m=>m.Id,m=>(m.Enabled,m.Priority),StringComparer.OrdinalIgnoreCase);
        var restoreDependencyBlockers=await GetDependencyBlockersAsync(restoreState,restore,ct);
        if(restoreDependencyBlockers.Length>0)
            throw new InvalidOperationException("Restore after safe mode blocked by dependency preflight. "+DescribeDependencyBlockers(restoreDependencyBlockers));
        var back=await s.Executor.ApplyAsync(restore,"Restore after safe mode",ct:ct);
        if(!back.Success)throw back.Exception??new InvalidOperationException(back.Message);
            StatusText="Safe mode ended and the applied mod configuration was restored.";
        });
    }

    [RelayCommand]
    private async Task ChooseConflictOption(string? winnerToken)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var conflict=SelectedConflict;
        if(conflict is null||string.IsNullOrWhiteSpace(winnerToken)){StatusText="Select a direct replacement and a winner first.";return;}
        if(!conflict.Options.Any(o=>StringComparer.OrdinalIgnoreCase.Equals(o.Token,winnerToken))){StatusText="That option is no longer part of the selected conflict.";return;}

        suppressChanged=true;
        try
        {
            foreach(var option in conflict.Options)SetConflictToken(option.Token,StringComparer.OrdinalIgnoreCase.Equals(option.Token,winnerToken));
        }
        finally{suppressChanged=false;Changed();}

        await RefreshAnalysis(CancellationToken.None);
        var chosen=conflict.Options.First(o=>StringComparer.OrdinalIgnoreCase.Equals(o.Token,winnerToken));
        StatusText=$"'{chosen.DisplayName}' will be used for this conflict. The alternatives will be turned off when you apply the pending changes.";
        await s.Timeline.RecordAsync("choice.conflict",AutomationSeverity.Info,$"User chose {chosen.DisplayName} for {conflict.Scope}.",new{conflict.BundleKey,chosen.Token,chosen.DisplayName},CancellationToken.None);
    }

    [RelayCommand]
    private async Task ChainConflictFamily(string? mainToken)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"mainToken={mainToken ?? "<null>"}");
        var conflict=SelectedConflict;
        if(conflict is null||string.IsNullOrWhiteSpace(mainToken)){StatusText="Select a conflict and the package that should be the family main first.";return;}
        var mainOption=conflict.Options.FirstOrDefault(o=>StringComparer.OrdinalIgnoreCase.Equals(o.Token,mainToken));
        if(mainOption is null){StatusText="That package is no longer part of the selected conflict.";return;}
        var memberPriority=Mods.SelectMany(x=>x.Members).ToDictionary(x=>x.Id,x=>x.Priority,StringComparer.OrdinalIgnoreCase);
        var childOptions=conflict.Options
            .Where(o=>!StringComparer.OrdinalIgnoreCase.Equals(o.Token,mainToken))
            .OrderBy(o=>o.MemberIds.Select(id=>memberPriority.GetValueOrDefault(id)).DefaultIfEmpty().Max())
            .ThenBy(o=>o.DisplayName,StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var childGroups=childOptions.Select(o=>(IReadOnlyList<string>)o.MemberIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()).ToArray();
        var childIds=childGroups.SelectMany(x=>x).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(childIds.Length==0){StatusText="There are no other conflicting packages to chain under this main mod.";return;}

        var mainIds=mainOption.MemberIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var descriptors=Mods.SelectMany(x=>x.Members).Where(m=>mainIds.Contains(m.Id,StringComparer.OrdinalIgnoreCase)).ToArray();
        var mainMemberId=descriptors
            .OrderBy(LogicalModFamilies.CompositionRank)
            .ThenBy(x=>x.Id,StringComparer.OrdinalIgnoreCase)
            .Select(x=>x.Id)
            .FirstOrDefault()??mainIds.First();
        var staged=CaptureStage();

        await RunBusy("family.manual-chain","Saving Mod Relationship",$"Treating '{mainOption.DisplayName}' as the main mod and attaching {childIds.Length} package(s) as add-ons…",true,async ct=>
        {
            var familyId=await s.Database.ChainManualFamilyAsync(mainMemberId,mainIds,childGroups,mainOption.DisplayName,ct);
            await ReloadMods(ct);

            // Family regrouping rebuilds the logical rows; restore the user's pending enable/disable
            // state so manually correcting a relationship never discards unrelated staged work.
            suppressChanged=true;
            try
            {
                foreach(var row in Mods)row.ApplyProfileState(staged);
            }
            finally{suppressChanged=false;Changed();}

            await RefreshAnalysis(ct);
            StatusText=$"Saved '{mainOption.DisplayName}' as the main mod with {childIds.Length} add-on package(s). The manager will remember this relationship for future conflict checks.";
            await s.Timeline.RecordAsync("family.manual-chain",AutomationSeverity.Info,$"User chained conflicting packages under {mainOption.DisplayName}.",new{conflict.BundleKey,familyId,mainMemberId,mainIds,childGroups},ct);
        });
    }

    private void SetConflictToken(string token,bool enabled)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(token.StartsWith("member:",StringComparison.OrdinalIgnoreCase))
        {
            var memberId=token["member:".Length..];
            var row=Mods.FirstOrDefault(x=>x.ContainsMember(memberId));
            row?.SetMemberEnabled(memberId,enabled);
            return;
        }
        var logical=Mods.FirstOrDefault(x=>StringComparer.OrdinalIgnoreCase.Equals(x.Id,token));
        if(logical is not null)logical.StagedEnabled=enabled;
    }

    private sealed record LogicalMemberIdentity(string MemberId,string LogicalId,string LogicalName,string? PreviewPath);
    private sealed record LogicalRowIdentity(string Id,string Name,bool Enabled,bool NeedsRevalidation,string? PreviewPath);
    private sealed class ChoiceAccumulator(string bundleKey)
    {
        public string BundleKey{get;}=bundleKey;
        public HashSet<string> Paths{get;}=new(StringComparer.OrdinalIgnoreCase);
        public List<ConflictDecision> Conflicts{get;}=[];
        public Dictionary<string,ConflictOptionRow> Options{get;}=new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task RunBusy(string operationName,string title,string detail,bool cancellable,Func<CancellationToken,Task> action)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod($"operation={operationName}; title={title}; cancellable={cancellable}");
        if(!programUpdateHandoffGate.TryBeginForeground())
        {
            MasterDebugLog.Write("UI-COMMAND", $"IGNORED operation={operationName}; updater handoff or another foreground operation owns the gate");
            return;
        }
        if(BusyVisibility==Visibility.Visible)
        {
            programUpdateHandoffGate.EndForeground();
            MasterDebugLog.Write("UI-COMMAND", $"IGNORED operation={operationName}; another operation is already busy");
            return;
        }
        busyCts=new CancellationTokenSource();
        BusyTitle=title;BusyDetail=detail;CancelVisibility=cancellable?Visibility.Visible:Visibility.Collapsed;CriticalOperation=!cancellable;BusyVisibility=Visibility.Visible;
        var sw=Stopwatch.StartNew();
        try
        {
            await s.Telemetry.TrackAsync(operationName,async(_,ct)=>await action(ct),
                new Dictionary<string,object?>{{"title",title},{"stagedMods",StagedCount}},busyCts.Token);
            FooterText=$"{title} finished in {sw.Elapsed.TotalSeconds:F1}s";
        }
        catch(OperationCanceledException){StatusText=$"{title} cancelled.";FooterText=StatusText;}
        catch(Exception ex)
        {
            var assessment=ExceptionPolicy.Assess(ex);
            StatusText=$"{assessment.UserSummary} {ex.Message}";
            FooterText=$"{title} failed — {assessment.Category}";
            s.Log.Error(ex,"UI operation {Operation} failed with category {Category}",operationName,assessment.Category);
        }
        finally
        {
            BusyVisibility=Visibility.Collapsed;CancelVisibility=Visibility.Collapsed;CriticalOperation=false;
            busyCts?.Dispose();busyCts=null;OnPropertyChanged(nameof(HeaderSummary));
            programUpdateHandoffGate.EndForeground();
        }
    }

    public void Dispose()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(disposed)return;
        disposed=true;
        backgroundCts.Cancel();
        backgroundCts.Dispose();
        deferredPageGate.Dispose();
        busyCts?.Cancel();
        busyCts?.Dispose();
        busyCts=null;
        searchCts?.Cancel();
        searchCts?.Dispose();
        searchCts=null;
        GC.SuppressFinalize(this);
    }

}
