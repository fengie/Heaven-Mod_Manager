using System.IO;
using System.Windows;
using System.Windows.Controls;
using MhwModManager.Automation;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class FomodInstallerWindow : Window
{
    private readonly FomodInstallerService installer;
    private readonly GameProfile game;
    private readonly StackPanel choices = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(8) };
    public HashSet<string> SelectedOptions { get; } = new(StringComparer.Ordinal);

    public FomodInstallerWindow(FomodInstallerService installer, GameProfile game, IEnumerable<string>? saved = null)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        this.installer = installer; this.game = game;
        Title = "Installer — " + installer.Name; Width = 820; Height = 720; MinWidth = 600; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (saved is not null) SelectedOptions.UnionWith(saved);
        var root = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var install = new Button { Content = "Install selected options", Margin = new Thickness(6) };
        install.Click += InstallClicked;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(6) };
        buttons.Children.Add(install); buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
        DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
        root.Children.Add(new ScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        RenderChoices();
    }
    private void RenderChoices()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        choices.Children.Clear();
        foreach (var step in installer.Describe(SelectedOptions).Where(s => s.Visible))
        {
            choices.Children.Add(new TextBlock { Text = step.Name, FontSize = 23, Margin = new Thickness(0, 12, 0, 8) });
            foreach (var group in step.Groups)
            {
                choices.Children.Add(new TextBlock { Text = group.Name + " · " + group.Type, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
                foreach (var option in group.Options)
                {
                    var check = new CheckBox { Content = option.Name + " (" + option.Type + ")", IsChecked = option.Selected, IsEnabled = option.Type is not ("Required" or "NotUsable") && group.Type != "SelectAll", Margin = new Thickness(8), ToolTip = option.Description };
                    check.Click += (_, _) =>
                    {
                        if (check.IsChecked == true)
                        {
                            if (group.Type is "SelectExactlyOne" or "SelectAtMostOne") foreach (var other in group.Options) SelectedOptions.Remove(other.Id);
                            SelectedOptions.Add(option.Id);
                        }
                        else SelectedOptions.Remove(option.Id);
                        try { RenderChoices(); } catch (InvalidDataException ex) { status.Text = ex.Message; }
                    };
                    choices.Children.Add(check);
                    if (!string.IsNullOrWhiteSpace(option.Description)) choices.Children.Add(new TextBlock { Text = option.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(30, 0, 8, 6), Opacity = 0.75 });
                }
            }
        }
        status.Text = "Choose options for every visible group. Required choices are included automatically.";
    }
    private void InstallClicked(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try { var files = installer.Plan(SelectedOptions, game); status.Text = $"{files.Count} files selected."; DialogResult = true; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { status.Text = ex.Message; }
    }
}
