using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using MhwModManager.Automation;
using MhwModManager.Core;
using MhwModManager.Storage;

namespace MhwModManager.App;

public partial class WorkflowWindow : Window
{
    private readonly AppServices services;
    private Dictionary<string, (bool enabled, int priority)> stage;
    private PlannerSnapshot? snapshot;
    private IReadOnlyList<EffectiveAsset> assets = [];
    private bool busy;
    private string folderPrefix = "";
    private string? recipePath;
    private string? editingRule;
    private (string older, string newer)? reviewedUpdate;
    public IReadOnlyDictionary<string, (bool enabled, int priority)>? RequestedStage { get; private set; }
    public bool AppliedMigration { get; private set; }

    public WorkflowWindow(AppServices services, Dictionary<string, (bool enabled, int priority)> stage)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.services = services; this.stage = stage;
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync(RefreshAsync);
        Closing += OnClosing;
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (busy) { e.Cancel = true; Status.Text = "Wait for the current operation to finish."; }
    }
    private async Task RunAsync(Func<Task> action)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (busy) return;
        busy = true; Tabs.IsEnabled = false; Status.Text = "Working…";
        try { await action(); }
        catch (Exception ex) { MasterDebugLog.Write("WORKFLOW", ex.ToString()); Status.Text = ex.Message; }
        finally { busy = false; Tabs.IsEnabled = true; }
    }
    private async Task RefreshAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        snapshot = await new PlannerSnapshotRepository(services.Database).LoadAsync();
        assets = await Task.Run(() => WorkflowAnalysis.Explore(WorkflowAnalysis.WithState(snapshot, stage), services.Planner));
        BuildFolders();
        FilterFiles();
        var mods = snapshot.Mods.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var box in new[] { LeftMod, RightMod, GraphMod, OldMod, NewMod })
        {
            var id = (box.SelectedItem as ModDescriptor)?.Id; box.ItemsSource = mods; box.SelectedItem = mods.FirstOrDefault(m => m.Id == id);
        }
        RuleRows.ItemsSource = snapshot.Rules;
        var profiles = await services.Profiles.ListAsync();
        foreach (var box in new[] { ParentProfile, ProfileA, ProfileB })
        {
            var id = (box.SelectedItem as ProfileSummary)?.Id; box.ItemsSource = profiles; box.SelectedItem = profiles.FirstOrDefault(p => p.Id == id);
        }
        var trust = new List<object>();
        foreach (var mod in mods)
        {
            var history = await services.Trust.GetAsync(mod.Id);
            trust.Add(new { Mod = mod.DisplayName, SuccessfulLaunches = history?.SuccessfulLaunches, FailureAssociations = history?.FailedLaunches, Rollbacks = history?.Rollbacks, LastSuccess = history?.LastSuccess, LastFailure = history?.LastFailure });
        }
        TrustRows.ItemsSource = trust;
        var adapter = GameAdapters.Resolve(services.Paths.Game); var game = services.Paths.Game;
        AdapterDetails.Text = $"{adapter.DisplayName} ({adapter.Id})\n\nExecutable: {adapter.Executable(game)}\nMod roots: {string.Join(", ", adapter.ModRoots(game))}\nNexus domain: {adapter.NexusDomain(game) ?? "none"}\nSave paths: {string.Join(", ", adapter.SavePaths(game))}\nMHW conflict semantics: {adapter.SupportsMhwConflictSemantics}\n\n" + string.Join("\n", adapter.Validate(game).Select(v => $"{v.Code}: {v.Message}"));
        reviewedUpdate = null; UpgradeButton.IsEnabled = false;
        Status.Text = $"{assets.Count} staged file paths. Rule edits affect future plans; live files change only through Apply or Upgrade safely.";
    }
    private async void RefreshClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(RefreshAsync);
    }
    private void FilterChanged(object sender, TextChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (Files is not null) FilterFiles();
    }
    private void FilterFiles()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var term = PathFilter.Text.Trim();
        Files.ItemsSource = assets.Where(a => a.Path.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase) && (term.Length == 0 || a.Path.Contains(term, StringComparison.OrdinalIgnoreCase) || a.Providers.Any(m => m.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)))).ToArray();
    }
    private void BuildFolders()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Folders.Items.Clear(); folderPrefix = "";
        var all = new TreeViewItem { Header = "All effective files", Tag = "", IsExpanded = true }; Folders.Items.Add(all);
        var folders = new Dictionary<string, TreeViewItem>(StringComparer.OrdinalIgnoreCase) { [""] = all };
        foreach (var asset in assets)
        {
            var segments = asset.Path.Split('\\'); var path = "";
            foreach (var segment in segments.SkipLast(1))
            {
                var parent = folders[path]; path += segment + "\\";
                if (folders.ContainsKey(path)) continue;
                var node = new TreeViewItem { Header = segment, Tag = path }; folders[path] = node; parent.Items.Add(node);
            }
        }
    }
    private void FolderSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        folderPrefix = (e.NewValue as TreeViewItem)?.Tag as string ?? ""; FilterFiles();
    }
    private void FileSelected(object sender, SelectionChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (Files.SelectedItem is not EffectiveAsset asset) return;
        Explanation.Text = $"{asset.Path}\nEffective provider: {asset.WinnerName ?? "No deployable winner"}\nSHA-256: {asset.Sha256 ?? "none"}\n\n" + string.Join("\n", asset.Evidence.Select(x => $"{(x.Decisive ? "Decision" : "Context")} · {x.Source}: {x.Description}"));
    }
    private async void OpenRecipe(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dialog = new OpenFileDialog { Filter = "Loadout recipes|*.ummpack;*.mhwrecipe;*.json" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(async () => { recipePath = null; var preview = await services.Recipe.PreviewAsync(dialog.FileName); RecipeRows.ItemsSource = preview.Matches; recipePath = dialog.FileName; Status.Text = $"{preview.Matches.Count(m => m.CanRestore)}/{preview.Matches.Count} entries can be restored. Inspect unresolved entries before saving."; });
    }
    private async void ExportRecipe(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var dialog = new SaveFileDialog { Filter = "Portable loadout|*.mhwrecipe|UMM recipe|*.ummpack", FileName = "loadout.mhwrecipe" };
        if (dialog.ShowDialog(this) != true) return;
        await RunAsync(async () => { await services.Recipe.ExportAsync(dialog.FileName); Status.Text = "Exported applied state and captured hashes: " + dialog.FileName; });
    }
    private async void ImportRecipe(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () =>
        {
            if (recipePath is null) throw new InvalidOperationException("Open a recipe first.");
            await EnsureNewProfileNameAsync(RecipeName.Text);
            var id = await services.Recipe.ImportAsync(recipePath, RecipeName.Text);
            await RefreshAsync(); ProfileB.SelectedItem = ((IReadOnlyList<ProfileSummary>)ProfileB.ItemsSource).First(p => p.Id == id);
            Status.Text = "Saved matched entries as a profile. Missing, mismatched, and ambiguous entries are OFF. Use Profiles & diff to stage it.";
        });
    }
    private async void RestoreRecipeFamilies(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () =>
        {
            if (recipePath is null) throw new InvalidOperationException("Open and review a recipe first.");
            var count = await services.Recipe.RestoreFamiliesAsync(recipePath); await RefreshAsync();
            Status.Text = $"Restored {count} matched family groups. Local rules were preserved; review effective files before Apply.";
        });
    }
    private async Task EnsureNewProfileNameAsync(string name)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if ((await services.Profiles.ListAsync()).Any(p => StringComparer.OrdinalIgnoreCase.Equals(p.Name, name.Trim()))) throw new InvalidOperationException("Choose a new profile name to preserve existing loadouts.");
    }
    private void ClearParent(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ParentProfile.SelectedItem = null;
    }
    private async void SaveProfile(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () => { await EnsureNewProfileNameAsync(ProfileName.Text); await services.Profiles.SaveAsync(ProfileName.Text, stage, (ParentProfile.SelectedItem as ProfileSummary)?.Id); await RefreshAsync(); Status.Text = "Saved staged setup. A child stores only differences from its parent."; });
    }
    private async void CompareProfiles(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () =>
        {
            if (ProfileA.SelectedItem is not ProfileSummary a || ProfileB.SelectedItem is not ProfileSummary b) throw new InvalidOperationException("Choose both profiles.");
            var left = await services.Profiles.LoadAsync(a.Id); var right = await services.Profiles.LoadAsync(b.Id);
            var current = await new PlannerSnapshotRepository(services.Database).LoadAsync();
            var diff = await Task.Run(() => WorkflowAnalysis.Compare(current, services.Planner, left, right));
            ProfileDiff.Text = $"{a.Name} → {b.Name}\n{diff.Mods.Count(m => !m.BeforeEnabled && m.AfterEnabled)} enabled · {diff.Mods.Count(m => m.BeforeEnabled && !m.AfterEnabled)} disabled · {diff.Mods.Count(m => m.BeforePriority != m.AfterPriority)} priority changes\n{diff.ProviderChanges.Count} effective providers change · {diff.IntroducedConflicts.Count} conflicts introduced · {diff.ResolvedConflicts.Count} resolved · {diff.ChangedArmorComponents.Count} armor components change\n\n" + string.Join("\n", diff.Mods.Select(m => $"{m.Name}: {(m.BeforeEnabled ? "ON" : "OFF")} → {(m.AfterEnabled ? "ON" : "OFF")}; priority {m.BeforePriority} → {m.AfterPriority}")) + "\n\nIntroduced conflicts:\n" + string.Join("\n", diff.IntroducedConflicts) + "\n\nProvider changes:\n" + string.Join("\n", diff.ProviderChanges);
            Status.Text = "Comparison uses current captured files and current global rules.";
        });
    }
    private async void StageProfile(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () => { if (ProfileB.SelectedItem is not ProfileSummary p) throw new InvalidOperationException("Choose the right profile."); stage = new(await services.Profiles.LoadAsync(p.Id), StringComparer.OrdinalIgnoreCase); RequestedStage = stage; await RefreshAsync(); Status.Text = "Profile staged. Close this window, review the plan, then Apply."; });
    }
    private void RuleSelected(object sender, SelectionChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (RuleRows.SelectedItem is not ConflictRule rule || snapshot is null) return;
        editingRule = rule.Id; RuleReason.Text = rule.Reason; RulePath.Text = rule.PathPattern ?? "";
        RuleKindBox.SelectedIndex = rule.Kind switch { RuleKind.Incompatible => 1, RuleKind.ExactWinner => 2, _ => 0 };
        LeftMod.SelectedItem = snapshot.Mods.FirstOrDefault(m => m.Id == (rule.LeftModId == rule.WinnerModId ? rule.RightModId : rule.LeftModId));
        RightMod.SelectedItem = snapshot.Mods.FirstOrDefault(m => m.Id == (rule.WinnerModId ?? rule.RightModId));
    }
    private void NewRule(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        editingRule = null; RuleRows.SelectedItem = null; RulePath.Text = ""; RuleReason.Text = "User compatibility decision";
    }
    private async void SaveRule(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () =>
        {
            if (RightMod.SelectedItem is not ModDescriptor right) throw new InvalidOperationException("Choose a winner / second mod.");
            var left = LeftMod.SelectedItem as ModDescriptor;
            var editor = new RulesEditorService(services.Database);
            switch (RuleKindBox.SelectedIndex)
            {
                case 3: await editor.SaveProviderAsync(RulePath.Text, right.Id); break;
                case 4:
                    if (left is null) throw new InvalidOperationException("Choose the base mod.");
                    await services.Database.ChainManualFamilyAsync(left.Id, [left.Id], [[right.Id]], string.IsNullOrWhiteSpace(RulePath.Text) ? left.DisplayName : RulePath.Text); break;
                case 5:
                    if (left is null || left.Id == right.Id) throw new InvalidOperationException("Choose two different mods.");
                    stage[right.Id] = (stage.GetValueOrDefault(right.Id, (right.Enabled, right.Priority)).Item1, checked(stage.GetValueOrDefault(left.Id, (left.Enabled, left.Priority)).Item2 + 1));
                    RequestedStage = stage; break;
                default:
                    var kind = RuleKindBox.SelectedIndex == 1 ? RuleKind.Incompatible : RuleKindBox.SelectedIndex == 2 ? RuleKind.ExactWinner : RuleKind.Overlay;
                    await editor.SaveAsync(new(editingRule ?? Guid.NewGuid().ToString("N"), kind, kind == RuleKind.ExactWinner ? RuleScope.ExactPath : RuleScope.ModPair, left?.Id, right.Id, kind == RuleKind.Incompatible ? null : right.Id, kind == RuleKind.ExactWinner ? RulePath.Text : null, RuleReason.Text, true, DateTimeOffset.UtcNow)); break;
            }
            editingRule = null; await RefreshAsync(); Status.Text = "Saved. Review the effective files before applying.";
        });
    }
    private async void DeleteRule(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () => { if (RuleRows.SelectedItem is not ConflictRule r) throw new InvalidOperationException("Select a rule."); await new RulesEditorService(services.Database).RemoveAsync(r.Id); editingRule = null; await RefreshAsync(); });
    }
    private async void ClearProvider(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () => { await new RulesEditorService(services.Database).SaveProviderAsync(RulePath.Text, null); await RefreshAsync(); });
    }
    private void GraphClick(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (snapshot is null || GraphMod.SelectedItem is not ModDescriptor focus) return;
        GraphCanvas.Children.Clear();
        var related = snapshot.Rules.Where(r => r.LeftModId == focus.Id || r.RightModId == focus.Id || r.WinnerModId == focus.Id).ToArray();
        var ids = related.SelectMany(r => new[] { r.LeftModId, r.RightModId, r.WinnerModId }).Where(id => id is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var m in snapshot.Mods.Where(m => focus.FamilyId is not null && m.FamilyId == focus.FamilyId)) ids.Add(m.Id);
        ids.Add(focus.Id);
        var nodes = snapshot.Mods.Where(m => ids.Contains(m.Id)).Take(50).ToArray();
        GraphCanvas.Height = Math.Max(600, nodes.Length * 85); var positions = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < nodes.Length; i++) positions[nodes[i].Id] = new(nodes[i].Id == focus.Id ? 70 : 580, nodes[i].Id == focus.Id ? 80 : i * 85 + 50);
        foreach (var r in related)
        {
            var winner = r.WinnerModId ?? r.RightModId; var loser = r.LeftModId == winner ? r.RightModId : r.LeftModId;
            if (loser is null || winner is null || !positions.TryGetValue(loser, out var from) || !positions.TryGetValue(winner, out var to)) continue;
            DrawEdge(from, to, r.Kind == RuleKind.Incompatible ? Brushes.IndianRed : Brushes.Goldenrod, r.Kind.ToString());
        }
        foreach (var node in nodes.Where(m => m.Id != focus.Id && focus.FamilyId is not null && m.FamilyId == focus.FamilyId)) DrawEdge(positions[focus.Id], positions[node.Id], Brushes.SteelBlue, "Family");
        var cycle = RuleGraph.FindCycle(snapshot.Rules)?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        foreach (var node in nodes)
        {
            var border = new Border { Width = 270, Height = 64, Padding = new Thickness(10), Background = Brushes.DarkSlateGray, BorderBrush = cycle.Contains(node.Id) ? Brushes.OrangeRed : Brushes.SlateGray, BorderThickness = new Thickness(2), Child = new TextBlock { Text = node.DisplayName + "\n" + (node.FamilyRole ?? "Package"), TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White } };
            Canvas.SetLeft(border, positions[node.Id].X); Canvas.SetTop(border, positions[node.Id].Y); GraphCanvas.Children.Add(border);
        }
        Status.Text = $"Showing {nodes.Length} related packages (up to 50). Red node borders indicate an explicit overlay cycle.";
    }
    private void DrawEdge(Point from, Point to, Brush color, string label)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var start = new Point(from.X + 135, from.Y + 32); var end = new Point(to.X + 135, to.Y + 32);
        var direction = end - start;
        if (direction.Length < 1) return;
        direction.Normalize();
        var offset = Math.Min(135 / Math.Max(Math.Abs(direction.X), 0.001), 32 / Math.Max(Math.Abs(direction.Y), 0.001));
        start += direction * offset; end -= direction * offset;
        GraphCanvas.Children.Add(new Line { X1 = start.X, Y1 = start.Y, X2 = end.X, Y2 = end.Y, Stroke = color, StrokeThickness = 2 });
        if (label == "Overlay")
        {
            var perpendicular = new Vector(-direction.Y, direction.X);
            GraphCanvas.Children.Add(new Polygon { Fill = color, Points = [end, end - direction * 12 + perpendicular * 5, end - direction * 12 - perpendicular * 5] });
        }
        var text = new TextBlock { Text = label, Foreground = color, Background = Brushes.Black };
        Canvas.SetLeft(text, (start.X + end.X) / 2); Canvas.SetTop(text, (start.Y + end.Y) / 2); GraphCanvas.Children.Add(text);
    }
    private async void PreviewUpgrade(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () =>
        {
            reviewedUpdate = null; UpgradeButton.IsEnabled = false;
            if (OldMod.SelectedItem is not ModDescriptor old || NewMod.SelectedItem is not ModDescriptor newer) throw new InvalidOperationException("Choose old and replacement packages.");
            var preview = await new UpdateMigrationService(services.Database, services.Planner, services.Executor).PreviewAsync(old.Id, newer.Id);
            var d = preview.Diff;
            UpdateDetails.Text = $"{old.DisplayName} → {newer.DisplayName}\n\n{d.Unchanged} unchanged · {d.Changed} modified · {d.Removed} removed · {d.Added} added\n{d.StructuralChanged} structural changes · {d.TextureChanged} texture changes\nPriority: preserve {old.Priority}\nRules transferable: {preview.TransferableRules}/{preview.TotalRules}\n\n" + string.Join("\n", preview.Warnings) + "\n\n" + string.Join("\n", preview.Plan.Conflicts.Where(c => c.Blocking).Select(c => c.Explanation)) + "\n\nUpgrade deploys the preview through the existing transaction journal. Family/rule transfer and supersession commit with the files; Undo restores both.";
            reviewedUpdate = (old.Id, newer.Id); UpgradeButton.IsEnabled = !preview.Plan.IsBlocked; Status.Text = preview.Plan.IsBlocked ? "Resolve blocking conflicts before upgrading." : "Preview ready. Upgrade changes the applied setup; unsaved staged edits must be applied or discarded first.";
        });
    }
    private async void Upgrade(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        await RunAsync(async () =>
        {
            if (reviewedUpdate is not { } reviewed || OldMod.SelectedItem is not ModDescriptor old || NewMod.SelectedItem is not ModDescriptor newer || old.Id != reviewed.older || newer.Id != reviewed.newer) throw new InvalidOperationException("Preview this pair first.");
            var current = await services.Database.GetModsAsync();
            if (current.Any(m => stage.TryGetValue(m.Id, out var value) && value != (m.Enabled, m.Priority))) throw new InvalidOperationException("Apply or discard staged changes before upgrading.");
            var processes = System.Diagnostics.Process.GetProcessesByName(services.Paths.Game.ProcessName);
            try { if (processes.Length > 0) throw new InvalidOperationException("Close the game before upgrading."); }
            finally { foreach (var process in processes) process.Dispose(); }
            var result = await new UpdateMigrationService(services.Database, services.Planner, services.Executor).UpgradeAsync(old.Id, newer.Id);
            if (!result.Success) throw result.Exception ?? new InvalidOperationException(result.Message);
            AppliedMigration = true; RequestedStage = null;
            stage = (await services.Database.GetModsAsync()).ToDictionary(m => m.Id, m => (m.Enabled, m.Priority), StringComparer.OrdinalIgnoreCase);
            await RefreshAsync(); Status.Text = result.Message + " Old package superseded; its source files remain available for Undo.";
        });
    }
}
