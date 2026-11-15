using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App;

/// <summary>
/// Results of an explicitly chosen folder/archive scan. Manual game assignment
/// is opt-in, only for one physical, structurally independent review candidate;
/// the intake service revalidates every source before publishing to an Inbox.
/// </summary>
public sealed class ModIntakeReviewWindow : Window
{
    private readonly List<GameModIntakeItem> items;
    private readonly TextBlock heading;
    private readonly DataGrid grid;
    private readonly StackPanel manualControls;
    private readonly ComboBox gameChoices;
    private readonly Button assignButton;
    private Func<GameModIntakeItem, string, Task<GameModIntakeScan>>? assignCandidate;
    private bool assigning;

    public ModIntakeReviewWindow(GameModIntakeScan report)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(report);
        items = report.Items.ToList();
        Title = "Mod pack — candidate review";
        Width = 980;
        Height = 600;
        MinWidth = 720;
        MinHeight = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(18) };
        heading = new TextBlock
        {
            Text = Summary(),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var note = new TextBlock
        {
            Text = "Routed mods are in the matching game's Inbox, not installed in a live game. " +
                "Unknown and conflicting packages remain untouched until you choose how to handle them. " +
                "Nested archive children and installer variants require separate review.",
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

        manualControls = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 12, 0, 0)
        };
        manualControls.Children.Add(new TextBlock
        {
            Text = "For a single review-only folder or archive: select its row, choose a registered game, " +
                "and confirm copying that package to the game's Inbox. Originals are preserved.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        });
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        gameChoices = new ComboBox
        {
            MinWidth = 220, MaxWidth = 340,
            DisplayMemberPath = nameof(GameProfile.DisplayName),
            Margin = new Thickness(0, 0, 10, 6)
        };
        AutomationProperties.SetName(gameChoices, "Assign reviewed mod to registered game");
        gameChoices.SelectionChanged += (_, _) => UpdateAssignmentAvailability();
        actions.Children.Add(gameChoices);
        assignButton = new Button
        {
            Content = "Assign selected package to Inbox", MinWidth = 220,
            MinHeight = 32, IsEnabled = false, Padding = new Thickness(12, 5, 12, 5)
        };
        AutomationProperties.SetName(assignButton, "Confirm game assignment for selected review candidate");
        assignButton.Click += AssignButton_Click;
        actions.Children.Add(assignButton);
        manualControls.Children.Add(actions);
        DockPanel.SetDock(manualControls, Dock.Bottom);
        root.Children.Add(manualControls);

        grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Single,
            ItemsSource = items
        };
        AutomationProperties.SetName(grid, "Mod intake candidate review");
        grid.SelectionChanged += (_, _) => UpdateAssignmentAvailability();
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

    public void EnableManualAssignment(
        IReadOnlyList<GameProfile> registeredGames,
        Func<GameModIntakeItem, string, Task<GameModIntakeScan>> onAssign)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(registeredGames);
        ArgumentNullException.ThrowIfNull(onAssign);
        var games = registeredGames.Where(game => GameProfile.IsCanonicalId(game.Id))
            .DistinctBy(game => game.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(game => game.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        if (games.Length == 0) return;
        assignCandidate = onAssign;
        gameChoices.ItemsSource = games;
        manualControls.Visibility = Visibility.Visible;
        UpdateAssignmentAvailability();
    }

    private string Summary()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var report = new GameModIntakeScan(items);
        return $"Detected {report.Items.Count} candidate result(s): {report.Routed} routed, " +
            $"{report.AlreadyPresent} already present, {report.NeedsReview} require review, " +
            $"{report.Skipped} skipped.";
    }

    private void UpdateAssignmentAvailability()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        assignButton.IsEnabled = !assigning && assignCandidate is not null &&
            gameChoices.SelectedItem is GameProfile &&
            grid.SelectedItem is GameModIntakeItem item &&
            item.Status == GameModIntakeStatus.NeedsReview &&
            !item.SourcePath.Contains("!/", StringComparison.Ordinal) &&
            (Directory.Exists(item.SourcePath) || File.Exists(item.SourcePath));
    }

    private async void AssignButton_Click(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (assigning || assignCandidate is null ||
            grid.SelectedItem is not GameModIntakeItem candidate ||
            gameChoices.SelectedItem is not GameProfile game ||
            candidate.Status != GameModIntakeStatus.NeedsReview)
            return;

        var confirm = MessageBox.Show(this,
            $"Copy this one reviewed package to {game.DisplayName}'s Inbox?\n\n" +
            $"{candidate.SourcePath}\n\nThis will not install the mod or change the original source.",
            "Confirm manual game assignment", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        var index = grid.SelectedIndex;
        assigning = true;
        UpdateAssignmentAvailability();
        try
        {
            var outcome = await assignCandidate(candidate, game.Id);
            var updated = outcome.Items.Single();
            if (updated.Status is not (GameModIntakeStatus.Routed or GameModIntakeStatus.AlreadyPresent))
                throw new InvalidDataException("Manual game assignment did not finish safely.");
            items[index] = updated;
            grid.Items.Refresh();
            grid.SelectedItem = updated;
            heading.Text = Summary();
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, "The assignment was cancelled. Check the review results before retrying.",
                "Mod assignment cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "The package was not confirmed for assignment: " + ex.Message,
                "Mod assignment needs review", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            assigning = false;
            UpdateAssignmentAvailability();
        }
    }
}
