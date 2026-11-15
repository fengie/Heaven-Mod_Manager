using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Explicit repair for previously imported collection wrappers. Never migrate,
    /// delete or change the identity of an existing package automatically.
    /// </summary>
    [RelayCommand]
    private async Task ReanalyzeManagedCollections()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var sources = Mods.Where(row => row.MemberCount == 1 && !row.AppliedEnabled &&
                !row.HasStagedChanges && Directory.Exists(row.SourcePath))
            .Select(row => row.SourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sources.Length == 0)
        {
            StatusText = "No disabled, unchanged library packages can be safely reanalyzed. " +
                "Existing enabled or pending-change mods are intentionally left alone.";
            return;
        }

        await RunBusy("mods.reanalyze-collections", "Separating Existing Mod Collections",
            "Inspecting tracked folders recursively without changing original mods or game files…",
            true, async ct =>
            {
                EnsureGameIntake();
                var intake = gameIntake!;
                var inspected = new List<(string Source, ModBoundaryResult Boundaries)>();
                var unrecognized = 0;
                foreach (var source in sources)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var boundaries = await Task.Run(
                            () => intake.PreviewManagedCollection(source, s.Paths.Game, ct), ct);
                        if (boundaries.Candidates.Count(candidate =>
                                candidate.Kind != ModBoundaryKind.Review) >= 2)
                            inspected.Add((source, boundaries));
                        else
                            unrecognized++;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or InvalidDataException or ArgumentException)
                    {
                        MasterDebugLog.Write("MOD-INTAKE",
                            "A tracked source could not be safely previewed for reanalysis.", ex);
                        unrecognized++;
                    }
                }

                var identifiable = inspected.Sum(item => item.Boundaries.Candidates.Count(
                    candidate => candidate.Kind != ModBoundaryKind.Review));
                if (identifiable == 0)
                {
                    StatusText = $"No provable multi-mod collections found across {sources.Length} " +
                        "disabled source packages. Nothing changed. Unknown or unsupported layouts " +
                        "remain intact and require manual review.";
                    return;
                }

                var uncertain = inspected.Sum(item => item.Boundaries.Candidates.Count(
                    candidate => candidate.Kind == ModBoundaryKind.Review));
                var example = string.Join(Environment.NewLine,
                    inspected.SelectMany(item => item.Boundaries.Candidates
                        .Where(candidate => candidate.Kind != ModBoundaryKind.Review)
                        .Take(4).Select(candidate => "• " + candidate.RelativePath))
                        .Take(8));
                var answer = MessageBox.Show(Application.Current.MainWindow,
                    $"Found {identifiable} independently identifiable mods in {inspected.Count} " +
                    $"existing collection(s), plus {uncertain} review-only groups." +
                    Environment.NewLine + Environment.NewLine + example +
                    Environment.NewLine + Environment.NewLine +
                    "Copy recognized child mods into their registered game Inboxes and import " +
                    "those Inbox packages? The original tracked collections, activation, " +
                    "load order and live game files remain unchanged. New packages start disabled. " +
                    "Existing pending Inbox packages will also be processed. " +
                    "Do not enable an old collection alongside its separated children.",
                    "Preview existing mod collection separation",
                    MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    StatusText = "Collection separation canceled before importing; originals unchanged.";
                    return;
                }

                var items = new List<GameModIntakeItem>();
                foreach (var item in inspected)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var report = await Task.Run(
                            () => intake.ImportManagedCollectionAsync(item.Source, s.Paths.Game, ct), ct);
                        items.AddRange(report.Items);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                               or InvalidDataException or ArgumentException)
                    {
                        MasterDebugLog.Write("MOD-INTAKE",
                            "A managed source could not be reanalyzed without review.", ex);
                        items.Add(new GameModIntakeItem(item.Source, null,
                            GameModIntakeStatus.NeedsReview, ex.Message));
                    }
                }

                var intakeReport = new GameModIntakeScan(items);
                var imported = 0;
                var inboxSkipped = 0;
                if (intakeReport.Routed > 0)
                {
                    var inbox = await s.Inbox.ProcessAsync(ct);
                    imported = inbox.Imported;
                    inboxSkipped = inbox.Skipped;
                    if (imported > 0)
                    {
                        await ReloadMods(ct);
                        await RefreshAnalysis(ct);
                    }
                }

                StatusText = $"Reanalysis: {intakeReport.Routed} children staged, " +
                    $"{intakeReport.AlreadyPresent} already present, " +
                    $"{intakeReport.NeedsReview} require review, " +
                    $"{imported} Inbox packages imported, {inboxSkipped} Inbox entries skipped; " +
                    $"{unrecognized} original packages did not qualify. Original tracked packages " +
                    "and game installations remain unchanged.";
                if (items.Count > 0)
                    new ModIntakeReviewWindow(intakeReport)
                    {
                        Owner = Application.Current.MainWindow
                    }.ShowDialog();
            });
    }
}
