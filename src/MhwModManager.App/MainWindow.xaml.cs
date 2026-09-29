using MhwModManager.Core;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MhwModManager.App.ViewModels;

namespace MhwModManager.App;

public partial class MainWindow:Window
{
    public MainWindow()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        InitializeComponent();
        DataContext=new MainWindowViewModel(App.Services);
        Closing+=OnClosing;
        Closed+=OnClosed;
        PreviewKeyDown+=OnPreviewKeyDown;
    }

    public Task InitializeAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ((MainWindowViewModel)DataContext).InitializeAsync();
    }

    public void StartProgramUpdater()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ((MainWindowViewModel)DataContext).StartProgramUpdater();
    }

    public Task RefreshAfterCatalogInstallAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ((MainWindowViewModel)DataContext).RefreshAfterCatalogInstallAsync();
    }

    private void OpenCatalogClick(object sender,RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var catalog=new CatalogWindow{Owner=this};
        catalog.ShowDialog();
    }


    private void OnPreviewKeyDown(object sender,KeyEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(e.Key!=Key.F||Keyboard.Modifiers!=ModifierKeys.Control)return;
        if(DataContext is MainWindowViewModel vm)vm.SelectedTab=1;
        ModSearchBox.Focus();
        ModSearchBox.SelectAll();
        e.Handled=true;
    }

    private void OnClosed(object? sender,EventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(DataContext is IDisposable disposable)disposable.Dispose();
    }

    private void OnClosing(object? sender,CancelEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(DataContext is not MainWindowViewModel vm||!vm.CriticalOperation)return;
        e.Cancel=true;
        MessageBox.Show(this,"A transactional or startup operation is still running. Closing now would emulate a process crash, so the normal Close action is blocked until it reaches a commit or rollback point.","Operation in progress",MessageBoxButton.OK,MessageBoxImage.Information);
    }
}
