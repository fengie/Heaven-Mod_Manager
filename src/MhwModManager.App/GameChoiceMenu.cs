using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using MhwModManager.Core;

namespace MhwModManager.App;

public sealed class GameChoiceMenu : MenuItem
{
    public static readonly DependencyProperty SwitchCommandProperty = DependencyProperty.Register(
        nameof(SwitchCommand), typeof(ICommand), typeof(GameChoiceMenu));

    public ICommand? SwitchCommand
    {
        get => (ICommand?)GetValue(SwitchCommandProperty);
        set => SetValue(SwitchCommandProperty, value);
    }

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        using var trace = MasterDebugLog.BeginMethod();
        base.PrepareContainerForItemOverride(element, item);
        if (element is MenuItem choice && item is GameProfile profile)
        {
            choice.SetBinding(CommandProperty, new Binding(nameof(SwitchCommand)) { Source = this });
            choice.CommandParameter = profile;
        }
    }

    protected override void ClearContainerForItemOverride(DependencyObject element, object item)
    {
        using var trace = MasterDebugLog.BeginMethod();
        if (element is MenuItem choice && item is GameProfile)
        {
            BindingOperations.ClearBinding(choice, CommandProperty);
            choice.ClearValue(CommandParameterProperty);
        }
        base.ClearContainerForItemOverride(element, item);
    }
}
