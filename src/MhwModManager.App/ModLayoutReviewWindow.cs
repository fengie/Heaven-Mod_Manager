using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MhwModManager.Automation;

namespace MhwModManager.App;

public sealed record ModLayoutReviewSource(
    string Source, ModLayoutInspection? Inspection, string? Error);

public sealed record ModLayoutReviewRow(
    string Source, string Group, string Profile, string Kind, int FilesCount,
    string Reason, IReadOnlyList<string> Files);

/// <summary>
/// Read-only visibility into every observed file in previously tracked library
/// packages. Patterns are evidence for review, never automatic import authority.
/// </summary>
public sealed class ModLayoutReviewWindow : Window
{
    public ModLayoutReviewWindow(IReadOnlyList<ModLayoutReviewSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Title = "Existing mod layouts — read-only preview";
        Width = 1100;
        Height = 690;
        MinWidth = 760;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var rows = new List<ModLayoutReviewRow>();
        foreach (var source in sources)
        {
            if (source.Inspection is not null)
            {
                foreach (var group in source.Inspection.Groups)
                    rows.Add(new ModLayoutReviewRow(source.Source, group.RelativePath,
                        source.Inspection.ProfileId, group.Kind.ToString(),
                        group.Files.Count, group.Reason, group.Files));
                if (source.Inspection.Groups.Count == 0)
                    rows.Add(new ModLayoutReviewRow(source.Source, ".",
                        source.Inspection.ProfileId, "Empty", 0,
                        "No files were observed in this tracked source package.", []));
            }
            else
            {
                rows.Add(new ModLayoutReviewRow(source.Source, ".", "unavailable",
                    "NotInspected", 0,
                    source.Error ?? "No file inventory was available for this tracked source.", []));
            }
        }

        var scanned = sources.Where(source => source.Inspection is not null).ToArray();
        var files = scanned.Sum(source => source.Inspection!.FileCount);
        var errors = sources.Count(source => source.Inspection is null);
        var heading = new TextBlock
        {
            Text = $"Inspected {scanned.Length} of {sources.Count} managed packages, " +
                $"{files:N0} files, {rows.Count:N0} result groups, {errors} inaccessible sources.",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var note = new TextBlock
        {
            Text = "No original files are changed. A filename companion pattern is not proof " +
                "of an independent mod. Review dependent components and optional variants " +
                "before importing. Existing deployment, activation and rollback state are untouched.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            ItemsSource = rows
        };
        AutomationProperties.SetName(grid, "Existing mod layouts and unclassified file groups");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "SOURCE PACKAGE",
            Binding = new Binding(nameof(ModLayoutReviewRow.Source)),
            Width = new DataGridLength(1.8, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "NESTED GROUP",
            Binding = new Binding(nameof(ModLayoutReviewRow.Group)),
            Width = new DataGridLength(2.3, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "EVIDENCE",
            Binding = new Binding(nameof(ModLayoutReviewRow.Kind)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "FILES",
            Binding = new Binding(nameof(ModLayoutReviewRow.FilesCount)),
            Width = new DataGridLength(0.55, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "REASON / REVIEW",
            Binding = new Binding(nameof(ModLayoutReviewRow.Reason)),
            Width = new DataGridLength(2.5, DataGridLengthUnitType.Star)
        });

        var detailsTitle = new TextBlock
        {
            Text = "Select a group above to inspect its exact relative file members.",
            Margin = new Thickness(0, 10, 0, 5)
        };
        var details = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Height = 150
        };
        AutomationProperties.SetName(details, "Selected group complete relative file list");
        grid.SelectionChanged += (_, _) =>
        {
            if (grid.SelectedItem is not ModLayoutReviewRow selected)
                return;
            details.Text = $"{selected.Source} — {selected.Group} ({selected.Profile})" +
                Environment.NewLine + selected.Reason + Environment.NewLine +
                string.Join(Environment.NewLine, selected.Files);
        };

        var close = new Button
        {
            Content = "Close preview",
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 120,
            MinHeight = 32,
            Padding = new Thickness(12, 5),
            Margin = new Thickness(0, 10, 0, 0)
        };
        close.Click += (_, _) => Close();

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel();
        header.Children.Add(heading);
        header.Children.Add(note);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var bottom = new StackPanel();
        bottom.Children.Add(detailsTitle);
        bottom.Children.Add(details);
        bottom.Children.Add(close);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(grid);
        Content = root;
    }
}
