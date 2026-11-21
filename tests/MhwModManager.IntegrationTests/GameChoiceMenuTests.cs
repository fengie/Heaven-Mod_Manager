using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using MhwModManager.App;
using MhwModManager.Core;
using Xunit;

namespace MhwModManager.IntegrationTests;

public sealed class GameChoiceMenuTests
{
    [Fact]
    public async Task ReplacingRowsAndBrowsingKeepActiveGameUntilAChoiceIsInvoked()
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { ExercisePicker(); completed.SetResult(); }
            catch (Exception error) { completed.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    private static void ExercisePicker()
    {
        var first = new GameProfile("first", "Same title", "first/root", "game.exe", "game", "generic", "");
        var second = first with { Id = "second", GameRoot = "second/root" };
        var choices = new ObservableCollection<GameProfile> { first, second };
        var requests = new List<GameProfile>();
        var picker = new GameChoiceMenu { ItemsSource = choices, Header = first.DisplayName,
            SwitchCommand = new RelayCommand<GameProfile>(game => requests.Add(game!)) };
        // Realize the native generated menu items in memory without opening a desktop popup.
        picker.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem">
                <ItemsPresenter/>
            </ControlTemplate>
            """);
        void Layout()
        {
            picker.ApplyTemplate();
            picker.Measure(new Size(500, 300));
            picker.Arrange(new Rect(0, 0, 500, 300));
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        }
        using var surface = new HwndSource(new HwndSourceParameters("Isolated game picker") { WindowStyle = 0, Width = 500, Height = 300 });
        surface.RootVisual = picker;
        Layout();
        choices.Clear();
        choices.Add(first with { });
        choices.Add(second with { });
        Layout();
        var row = Assert.IsType<MenuItem>(picker.ItemContainerGenerator.ContainerFromIndex(1));
        Assert.Same(choices[1], row.CommandParameter);
        // Native keyboard traversal changes the highlighted item; only an invoke activates it.
        void KeyDown(Key key) => row.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, surface, 0, key)
            { RoutedEvent = Keyboard.KeyDownEvent });
        KeyDown(Key.Down);
        Assert.Empty(requests);
        var peer = new MenuItemAutomationPeer(row);
        ((IInvokeProvider)peer).Invoke();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Assert.Same(choices[1], Assert.Single(requests));
        Assert.Equal(first.DisplayName, picker.Header);
        requests.Clear();
        KeyDown(Key.Enter);
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        Assert.Same(choices[1], Assert.Single(requests));
    }
}
