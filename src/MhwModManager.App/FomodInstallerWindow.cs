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
        Title = "Install Mod — " + installer.Name; Width = 820; Height = 720; MinWidth = 600; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (saved is not null) SelectedOptions.UnionWith(saved);
        var root = new DockPanel { Margin = new Thickness(20) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var install = new Button { Content = "Install Mod", Margin = new Thickness(6), IsDefault = true };
        install.SetResourceReference(Button.StyleProperty, "PrimaryButton");
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
                var groupRule = group.Type switch
                {
                    "SelectExactlyOne" => "Choose one",
                    "SelectAtMostOne" => "Choose up to one",
                    "SelectAll" => "Included together",
                    _ => group.Type
                };
                choices.Children.Add(new TextBlock { Text = group.Name + " · " + groupRule, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 4) });
                foreach (var option in group.Options)
                {
                    var optionState = option.Type switch
                    {
                        "Required" => "Required",
                        "NotUsable" => "Unavailable",
                        _ => string.Empty
                    };
                    var optionLabel = string.IsNullOrWhiteSpace(optionState) ? option.Name : option.Name + " · " + optionState;
                    var check = new CheckBox { Content = optionLabel, IsChecked = option.Selected, IsEnabled = option.Type is not ("Required" or "NotUsable") && group.Type != "SelectAll", Margin = new Thickness(8), ToolTip = option.Description };
                    check.Click += (_, _) =>
                    {
                        if (check.IsChecked == true)
                        {
                            if (group.Type is "SelectExactlyOne" or "SelectAtMostOne") foreach (var other in group.Options) SelectedOptions.Remove(other.Id);
                            SelectedOptions.Add(option.Id);
                        }
                        else SelectedOptions.Remove(option.Id);
                        try { RenderChoices(); } catch (InvalidDataException ex) { status.Text = "This installer needs a different choice: " + ex.Message; }
                    };
                    choices.Children.Add(check);
                    if (!string.IsNullOrWhiteSpace(option.Description)) choices.Children.Add(new TextBlock { Text = option.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(30, 0, 8, 6), Opacity = 0.75 });
                }
            }
        }
        status.Text = "Choose the features you want. Required items are already included for you.";
    }
    private void InstallClicked(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        try { var files = installer.Plan(SelectedOptions, game); status.Text = $"Ready to install {files.Count} file(s)."; DialogResult = true; }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException) { status.Text = "The mod cannot be installed with these choices: " + ex.Message; }
    }
}
