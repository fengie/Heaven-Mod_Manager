using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App;

/// <summary>
/// Read-only report after an explicitly chosen folder or mod pack was examined.
/// Classification and actual routing remain owned by GameModIntakeService.
/// Unknown candidates are never assigned or installed by this window.
/// </summary>
public sealed class ModIntakeReviewWindow : Window
{
    public ModIntakeReviewWindow(GameModIntakeScan report)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(report);
        Title = "Mod pack — candidate review";
        Width = 980;
        Height = 600;
        MinWidth = 720;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(18) };
        var heading = new TextBlock
        {
            Text = $"Detected {report.Items.Count} candidate result(s): {report.Routed} routed, " +
                $"{report.AlreadyPresent} already present, {report.NeedsReview} require review, " +
                $"{report.Skipped} skipped.",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var note = new TextBlock
        {
            Text = "Read-only results from your selected input. Routed mods are in the matching " +
                "game Inbox, not installed in a live game. Unknown or conflicting packages were " +
                "left untouched; verify them before importing manually.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);

        var close = new Button
        {
            Content = "Close review", IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinWidth = 110, MinHeight = 32, Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            ItemsSource = report.Items.ToArray()
        };
        AutomationProperties.SetName(grid, "Mod intake candidate review");
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "SOURCE / ARCHIVE CHILD",
            Binding = new Binding(nameof(GameModIntakeItem.SourcePath)),
            Width = new DataGridLength(2.6, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "TARGET GAME",
            Binding = new Binding(nameof(GameModIntakeItem.GameId))
                { TargetNullValue = "Unassigned" },
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "RESULT",
            Binding = new Binding(nameof(GameModIntakeItem.Status)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "WHY / NEXT REVIEW",
            Binding = new Binding(nameof(GameModIntakeItem.Reason)),
            Width = new DataGridLength(2.5, DataGridLengthUnitType.Star)
        });
        root.Children.Add(grid);
        Content = root;
    }
}
