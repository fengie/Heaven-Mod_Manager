using System.Windows;
using System.Windows.Controls;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class FamilyOverwriteReviewWindow : Window
{
    public FamilyOverwriteReviewWindow(string heading, string warning, IReadOnlyList<string> sharedPaths)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        Title = "Review complete family overwrite scope";
        Width = 780;
        Height = 560;
        MinWidth = 420;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel { Margin = new Thickness(16) };
        var header = new TextBlock
        {
            Text = heading + warning + $"\n\nCurrent overlapping target paths ({sharedPaths.Count}):",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        };
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, Margin = new Thickness(0, 0, 8, 0) };
        var attach = new Button { Content = "Attach as add-on", MinWidth = 120 };
        attach.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(attach);
        DockPanel.SetDock(actions, Dock.Bottom);
        layout.Children.Add(actions);
        var paths = new ListBox { ItemsSource = sharedPaths };
        ScrollViewer.SetHorizontalScrollBarVisibility(paths, ScrollBarVisibility.Auto);
        VirtualizingPanel.SetIsVirtualizing(paths, true);
        layout.Children.Add(paths);
        Content = layout;
    }
}
