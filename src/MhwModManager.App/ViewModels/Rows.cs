using CommunityToolkit.Mvvm.ComponentModel;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public partial class ModPartRowViewModel:ObservableObject
{
    private readonly Action<string,bool> changed;
    public string MemberId{get;}
    public string Label{get;}
    public string Role{get;}
    public string Source{get;}
    public string? NexusLabel{get;}
    [ObservableProperty]private bool enabled;
    private bool suppress;

    public ModPartRowViewModel(ModDescriptor mod,string label,bool initial,Action<string,bool> onChanged)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        MemberId=mod.Id;Label=label;Role=DescribeRole(mod);Source=mod.SourcePath;changed=onChanged;enabled=initial;
        NexusLabel=!string.IsNullOrWhiteSpace(mod.NexusModId)?$"Nexus {mod.NexusModId} • {mod.NexusCategory}":null;
    }
    partial void OnEnabledChanged(bool value){if(!suppress)changed(MemberId,value);}
    public void SetSilently(bool value){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        suppress=true;try{Enabled=value;}finally{suppress=false;}}

    private static string DescribeRole(ModDescriptor mod)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(StringComparer.OrdinalIgnoreCase.Equals(mod.FamilyRole,"Main"))return "Main (manual)";
        if(StringComparer.OrdinalIgnoreCase.Equals(mod.FamilyRole,"Optional"))return "Optional (manual)";
        if(StringComparer.OrdinalIgnoreCase.Equals(mod.FamilyRole,"Component"))return "Component (manual)";
        if(mod.NexusCategory==NexusFileCategory.Update)return "Update";
        if(mod.NexusCategory==NexusFileCategory.Optional)return "Optional";
        var n=mod.DisplayName.ToLowerInvariant();
        if(n.Contains("hotfix",StringComparison.Ordinal))return "Hotfix";
        if(n.Contains("fix",StringComparison.Ordinal))return "Fix";
        if(n.Contains("patch",StringComparison.Ordinal))return "Patch";
        if(n.Contains("texture",StringComparison.Ordinal)||n.Contains("skin",StringComparison.Ordinal))return "Texture";
        return "Component";
    }
}

public partial class ModRowViewModel:ObservableObject
{
    private readonly Action _changed;
    private readonly Dictionary<string,(bool enabled,int priority)> _appliedMembers;
    private readonly Dictionary<string,(bool enabled,int priority)> _stagedMembers;
    private bool _updatingSummary;

    public string Id{get;}
    public string Name{get;}
    public string DisplayName{get;}
    public string SourcePath{get;}
    public string SourceSummary{get;}
    public string SourceName{get;}
    public string IdentityHint{get;}
    public string? Category{get;}
    public IReadOnlyList<ModDescriptor> Members{get;}
    public IReadOnlyList<ModDescriptor> ArchivedMembers{get;}
    public ObservableRangeCollection<ModPartRowViewModel> Parts{get;}=[];
    public ObservableRangeCollection<string> GalleryPaths{get;}=[];
    public int MemberCount=>Members.Count;
    public int ArchivedCount=>ArchivedMembers.Count;
    public bool IsComposite=>MemberCount>1;
    public int Priority=>_stagedMembers.Count==0?0:_stagedMembers.Values.Max(x=>x.priority);
    public bool AppliedEnabled=>_appliedMembers.Values.Any(x=>x.enabled);
    public int AppliedEnabledMembers=>_appliedMembers.Values.Count(x=>x.enabled);
    public bool HasStagedChanges=>_stagedMembers.Any(x=>!_appliedMembers.TryGetValue(x.Key,out var old)||old!=x.Value);
    public int StagedEnabledMembers=>_stagedMembers.Values.Count(x=>x.enabled);
    public bool WillEnable=>HasStagedChanges&&StagedEnabledMembers>AppliedEnabledMembers;
    public bool WillDisable=>HasStagedChanges&&StagedEnabledMembers<AppliedEnabledMembers;
    public string PartsLabel
    {
        get
        {
            var baseLabel=IsComposite?$"{MemberCount} parts • {BuildPartsLabel()}":"Single package";
            return ArchivedCount>0?$"{baseLabel} • {ArchivedCount} older revision(s) archived":baseLabel;
        }
    }
    public string StateLabel=>HasStagedChanges
        ? StagedEnabledMembers switch
        {
            0=>"Will disable",
            var n when n==MemberCount=>"Will enable",
            _=>$"Will enable {StagedEnabledMembers}/{MemberCount} parts"
        }
        : StagedEnabledMembers switch
        {
            0=>"Disabled",
            var n when n==MemberCount=>"Enabled",
            _=>$"Partially enabled {StagedEnabledMembers}/{MemberCount}"
        };

    [ObservableProperty]private bool? stagedEnabled;
    [ObservableProperty]private EffectiveModState effectiveState=EffectiveModState.Disabled;
    [ObservableProperty]private string effectiveReason="Not analyzed yet.";
    [ObservableProperty]private int winningFiles;
    [ObservableProperty]private int shadowedFiles;
    [ObservableProperty]private string? issueBadge;
    [ObservableProperty]private string? issueReason;
    [ObservableProperty]private int issueScore;
    [ObservableProperty]private string? thumbnailPath;
    [ObservableProperty]private string? updateBadge;
    public bool HasVisuals=>GalleryPaths.Count>0||!string.IsNullOrWhiteSpace(ThumbnailPath);
    public bool HasUpdate=>!string.IsNullOrWhiteSpace(UpdateBadge);
    public bool HasIssue=>!string.IsNullOrWhiteSpace(IssueBadge);
    public string EffectLabel=>EffectiveState switch
    {
        EffectiveModState.FullyEffective=>"Working",
        EffectiveModState.PartiallyOverridden=>$"Partly overridden • {ShadowedFiles} file(s)",
        EffectiveModState.FullySuperseded=>"Not currently used",
        EffectiveModState.NeedsChoice=>"Needs your choice",
        EffectiveModState.NeedsRevalidation=>"Needs a check",
        _=>"Disabled"
    };

    public ModRowViewModel(LogicalModFamily family,Action changed)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Id=family.Id;Name=family.Name;DisplayName=family.DisplayName;Category=family.Category;Members=family.Members;ArchivedMembers=family.SupersededMembers;
        SourcePath=family.Members.Count==1?family.Members[0].SourcePath:string.Empty;
        SourceSummary=family.Members.Count==1?family.Members[0].SourcePath:$"{family.Members.Count} source packages";
        SourceName=family.Members.Count==1?System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(SourcePath)):$"{family.Members.Count} source packages";
        if(string.IsNullOrWhiteSpace(SourceName))SourceName=DisplayName;
        var identity=SourceName;
        if(StringComparer.OrdinalIgnoreCase.Equals(identity,DisplayName))identity="Single package";
        else if(identity.StartsWith(DisplayName,StringComparison.OrdinalIgnoreCase))
        {
            var suffix=identity[DisplayName.Length..].Trim(' ','-',':','|','_','–','—');
            if(!string.IsNullOrWhiteSpace(suffix))identity=suffix;
        }
        IdentityHint=IsComposite?PartsLabel:identity;
        _appliedMembers=family.Members.ToDictionary(x=>x.Id,x=>(x.Enabled,x.Priority),StringComparer.OrdinalIgnoreCase);
        _stagedMembers=new(_appliedMembers,StringComparer.OrdinalIgnoreCase);_changed=changed;
        ThumbnailPath=family.Members.Select(m=>m.PreviewPath).FirstOrDefault(p=>!string.IsNullOrWhiteSpace(p)&&System.IO.File.Exists(p));
        foreach(var member in Members)Parts.Add(new ModPartRowViewModel(member,PartName(member.DisplayName),_stagedMembers[member.Id].enabled,OnPartChanged));
        SetSummaryFromMembers();
    }

    partial void OnStagedEnabledChanged(bool? value)
    {
        if(_updatingSummary)return;
        if(value.HasValue)foreach(var id in _stagedMembers.Keys.ToArray()){var current=_stagedMembers[id];_stagedMembers[id]=(value.Value,current.priority);}
        SyncParts();RaiseStateChanged();_changed();
    }

    public IReadOnlyDictionary<string,(bool enabled,int priority)> ExpandStage()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return _stagedMembers;
    }
    public IReadOnlyList<ModDescriptor> StagedMemberDescriptors()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return Members.Where(m=>_stagedMembers.TryGetValue(m.Id,out var state)&&state.enabled).Select(m=>m with{Enabled=true,Priority=_stagedMembers[m.Id].priority}).ToArray();
    }

    public void SetMemberEnabled(string memberId,bool enabled)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!_stagedMembers.TryGetValue(memberId,out var current))return;_stagedMembers[memberId]=(enabled,current.priority);SetSummaryFromMembers();_changed();
    }
    public bool ContainsMember(string memberId)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return _stagedMembers.ContainsKey(memberId);
    }

    public void ApplyProfileState(IReadOnlyDictionary<string,(bool enabled,int priority)> state)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach(var member in Members)_stagedMembers[member.Id]=state.TryGetValue(member.Id,out var value)?value:(false,member.Priority);
        SetSummaryFromMembers();_changed();
    }
    public void CommitApplied(){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        _appliedMembers.Clear();foreach(var item in _stagedMembers)_appliedMembers[item.Key]=item.Value;RaiseStateChanged();}

    public void DiscardStaged()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        _stagedMembers.Clear();
        foreach(var item in _appliedMembers)_stagedMembers[item.Key]=item.Value;
        SetSummaryFromMembers();
        _changed();
    }

    public void SetEffective(EffectiveModSummary summary)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        EffectiveState=summary.State;EffectiveReason=summary.Reason;WinningFiles=summary.WinningFiles;ShadowedFiles=summary.ShadowedFiles;OnPropertyChanged(nameof(EffectLabel));
    }

    public void SetIssue(string? badge,int score,string? reason)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        IssueBadge=badge;IssueScore=score;IssueReason=reason;OnPropertyChanged(nameof(HasIssue));
    }

    public void SetVisuals(IEnumerable<string> paths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var items=paths.Where(System.IO.File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        GalleryPaths.ReplaceAll(items);
        if(items.Length>0)ThumbnailPath=items[0];
        OnPropertyChanged(nameof(HasVisuals));
    }

    public void SetUpdateBadge(string? text)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        UpdateBadge=text;OnPropertyChanged(nameof(HasUpdate));
    }

    private void OnPartChanged(string id,bool enabled){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!_stagedMembers.TryGetValue(id,out var state))return;_stagedMembers[id]=(enabled,state.priority);SetSummaryFromMembers();_changed();}
    private void SyncParts(){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        foreach(var part in Parts)if(_stagedMembers.TryGetValue(part.MemberId,out var state))part.SetSilently(state.enabled);}
    private void SetSummaryFromMembers(){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        _updatingSummary=true;try{var enabled=StagedEnabledMembers;StagedEnabled=enabled==0?false:enabled==MemberCount?true:null;}finally{_updatingSummary=false;}SyncParts();RaiseStateChanged();}
    private void RaiseStateChanged(){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        OnPropertyChanged(nameof(Priority));OnPropertyChanged(nameof(AppliedEnabled));OnPropertyChanged(nameof(AppliedEnabledMembers));OnPropertyChanged(nameof(HasStagedChanges));OnPropertyChanged(nameof(StagedEnabledMembers));OnPropertyChanged(nameof(WillEnable));OnPropertyChanged(nameof(WillDisable));OnPropertyChanged(nameof(StateLabel));OnPropertyChanged(nameof(PartsLabel));}
    private string BuildPartsLabel(){var labels=Members.Select(m=>PartName(m.DisplayName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();return labels.Length<=4?string.Join(" + ",labels):string.Join(" + ",labels.Take(4))+$" +{labels.Length-4}";}
    private string PartName(string value){
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(StringComparer.OrdinalIgnoreCase.Equals(value,DisplayName))return "Main";if(value.StartsWith(DisplayName,StringComparison.OrdinalIgnoreCase)){var suffix=value[DisplayName.Length..].Trim(' ','-',':','|','–','—');if(suffix.Length>0)return suffix;}return value;}
}

public sealed record ConflictOptionRow(string Token,string DisplayName,string? PreviewPath,string Detail,IReadOnlyList<string> MemberIds);

public sealed record ConflictRow(
    string BundleKey,
    string Path,
    IReadOnlyList<string> Paths,
    int FileCount,
    ConflictKind Kind,
    string Explanation,
    Confidence Confidence,
    int ResolverScore,
    string Evidence,
    IReadOnlyList<ConflictOptionRow> Options)
{
    public string Providers=>string.Join("  ↔  ",Options.Select(x=>x.DisplayName));
    public string Scope=>FileCount<=1?AssetBundles.DisplayNameForPath(Path):$"{FileCount} files • {AssetBundles.DisplayNameForPath(Path)}";
    public string FileCountLabel=>FileCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string ConfidenceLabel=>$"{Confidence} confidence";
}

public sealed record OutfitRow(string Armor,string ModelId,int Available,string WinningPieces,string Status,string? PreviewPath,string Providers);
public sealed record AssetOverlapRow(string AssetKey,string DisplayName,int ProviderCount,string Providers,string Resolution,string Detail,string PrimaryPath);
public sealed record ActivityRow(string Id,string State,string Description,string Started);
public sealed record ModIssueRow(string ModId,string DisplayName,string Kind,int Score,string Reason,string LastSeen,int FailureCount,bool Confirmed)
{
    public string ConfidenceLabel=>Confirmed?"Confirmed by test":$"Evidence {Score}%";
    public string FailureLabel=>FailureCount==1?"1 report":$"{FailureCount} reports";
}

