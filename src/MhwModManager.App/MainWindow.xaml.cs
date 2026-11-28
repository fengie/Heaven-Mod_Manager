using MhwModManager.Core;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MhwModManager.App.ViewModels;

namespace MhwModManager.App;

public partial class MainWindow:Window
{
    public MainWindow()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        InitializeComponent();
        DataContext=new MainWindowViewModel(App.Services);
        Activated+=(_,_)=>{ if(DataContext is MainWindowViewModel vm)vm.ResumeDeferredDiscovery(); };
        Closing+=OnClosing;
        Closed+=OnClosed;
        PreviewKeyDown+=OnPreviewKeyDown;
        SourceInitialized+=OnSourceInitialized;
    }

    public Task InitializeAsync()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return ((MainWindowViewModel)DataContext).InitializeAsync();
    }

    public async Task ReplaceGameSessionAsync(AppServices next, Action commit)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(commit);
        if (DataContext is not MainWindowViewModel previousViewModel)
            throw new InvalidOperationException("Cannot switch game without an active view model.");

        // The WPF window and process identity survive. A candidate may fail to initialize;
        // in that case restore the old bindings and do not touch the persisted active game.
        var replacement = new MainWindowViewModel(next);
        try
        {
            DataContext = replacement;
            await replacement.InitializeAsync();
            commit();
        }
        catch
        {
            DataContext = previousViewModel;
            try { await replacement.StopPageReadsAsync(); }
            finally { replacement.Dispose(); }
            throw;
        }

        // This is after commit; failures during old-session cleanup must not roll back the
        // new active window/database to a disposed previous session.
        try
        {
            try { await previousViewModel.StopPageReadsAsync(); }
            finally { previousViewModel.Dispose(); }
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write("GAME-SWITCH", "Previous game view-model disposal failed after commit.", ex);
        }
        ConfigureWorkspaceLayout();
        replacement.StartProgramUpdater();
    }

    public void StartProgramUpdater()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ((MainWindowViewModel)DataContext).StartProgramUpdater();
    }


    private void OnSourceInitialized(object? sender,EventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var handle=new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if(handle==IntPtr.Zero)return;

        var enabled=1;
        const int useImmersiveDarkMode=20;
        const int useImmersiveDarkModeBefore20H1=19;
        if(DwmSetWindowAttribute(handle,useImmersiveDarkMode,ref enabled,sizeof(int))!=0)
            _=DwmSetWindowAttribute(handle,useImmersiveDarkModeBefore20H1,ref enabled,sizeof(int));
    }

    #pragma warning disable SYSLIB1054 // This single blittable System32 call avoids enabling unsafe code for the WPF app just to use LibraryImport.
    [DllImport("dwmapi.dll",ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(nint hwnd,int dwAttribute,ref int pvAttribute,int cbAttribute);
    #pragma warning restore SYSLIB1054

    private void OnWindowLoaded(object sender,RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ConfigureWorkspaceLayout();
        AnimateElement(RootLayout,0.94,0,6,170);
    }

    private ColumnDefinition? workspaceSidebarColumn;
    private GridSplitter? workspaceSidebarSplitter;
    private WorkspaceTabPanel? workspaceHeaderPanel;

    private void ConfigureWorkspaceLayout()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (DataContext is not MainWindowViewModel vm) return;
        MainTabs.ApplyTemplate();
        var template = MainTabs.Template;
        if (template?.FindName("PART_NavigationWidth", MainTabs) is not ColumnDefinition column ||
            template.FindName("PART_NavigationSplitter", MainTabs) is not GridSplitter splitter ||
            template.FindName("PART_NavigationHeaderPanel", MainTabs) is not WorkspaceTabPanel headers) return;
        if (!ReferenceEquals(workspaceSidebarSplitter, splitter))
        {
            if (workspaceSidebarSplitter is not null)
            {
                workspaceSidebarSplitter.DragCompleted -= OnWorkspaceSplitterCompleted;
                workspaceSidebarSplitter.KeyUp -= OnWorkspaceSplitterKeyUp;
            }
            workspaceSidebarSplitter = splitter;
            workspaceSidebarSplitter.DragCompleted += OnWorkspaceSplitterCompleted;
            workspaceSidebarSplitter.KeyUp += OnWorkspaceSplitterKeyUp;
        }
        if (!ReferenceEquals(workspaceHeaderPanel, headers))
        {
            if (workspaceHeaderPanel is not null) workspaceHeaderPanel.OrderChanged -= OnWorkspaceOrderChanged;
            workspaceHeaderPanel = headers;
            workspaceHeaderPanel.OrderChanged += OnWorkspaceOrderChanged;
        }
        workspaceSidebarColumn = column;
        ApplyWorkspaceLayout(vm.ReadWorkspaceLayout());
    }

    private void ApplyWorkspaceLayout(WorkspaceLayout layout)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        layout = WorkspaceLayout.Normalize(layout);
        if (workspaceSidebarColumn is not null) workspaceSidebarColumn.Width = new GridLength(layout.SidebarWidth);
        workspaceHeaderPanel?.SetOrder(layout.NavigationOrder);
        ModToolsRow.Height = layout.ModToolsHeight is double height ? new GridLength(height) : GridLength.Auto;
    }

    private void PersistWorkspaceLayout()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (DataContext is not MainWindowViewModel vm ||
            workspaceSidebarColumn is null || workspaceHeaderPanel is null) return;
        var width = workspaceSidebarColumn.Width.IsAbsolute ? workspaceSidebarColumn.Width.Value : workspaceSidebarColumn.ActualWidth;
        var height = ModToolsRow.Height.IsAbsolute ? (double?)ModToolsRow.Height.Value : null;
        vm.SaveWorkspaceLayout(new WorkspaceLayout(workspaceHeaderPanel.Order, width, height));
    }

    private void OnWorkspaceOrderChanged(object? sender, EventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        PersistWorkspaceLayout();
    }

    private void OnWorkspaceSplitterCompleted(object sender, DragCompletedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!e.Canceled) PersistWorkspaceLayout();
    }

    private void OnWorkspaceSplitterKeyUp(object sender, KeyEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        PersistWorkspaceLayout();
    }

    private void OnResetWorkspaceLayout(object sender, RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        ApplyWorkspaceLayout(WorkspaceLayout.Default);
        PersistWorkspaceLayout();
    }

    private void OnMainTabSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(!ReferenceEquals(e.Source,MainTabs)||!IsLoaded)return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(() =>
        {
            if(MainTabs.Template.FindName("PART_SelectedContentHost",MainTabs) is FrameworkElement content)
                AnimateElement(content,0.78,10,0,155);
        }));
    }

    private void OnBusyOverlayIsVisibleChanged(object sender,DependencyPropertyChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(e.NewValue is not true)
        {
            ResetBusyMotion();
            return;
        }

        AnimateElement(BusyOverlay,0.42,0,0,130);
        if(BusyDialogCard.RenderTransform is not ScaleTransform scale)return;
        if(!UiMotion.AnimationsEnabled||!SystemParameters.ClientAreaAnimation)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            scale.ScaleX=1;
            scale.ScaleY=1;
            return;
        }

        var duration=new Duration(TimeSpan.FromMilliseconds(155));
        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation{From=0.975,To=1,Duration=duration,EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation{From=0.975,To=1,Duration=duration,EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
    }

    private static void AnimateElement(FrameworkElement element,double fromOpacity,double fromX,double fromY,int durationMs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var translate=element.RenderTransform as TranslateTransform;
        if(translate is null)
        {
            translate=new TranslateTransform();
            element.RenderTransform=translate;
        }

        if(!UiMotion.AnimationsEnabled||!SystemParameters.ClientAreaAnimation)
        {
            element.BeginAnimation(UIElement.OpacityProperty,null);
            translate.BeginAnimation(TranslateTransform.XProperty,null);
            translate.BeginAnimation(TranslateTransform.YProperty,null);
            element.Opacity=1;
            translate.X=0;
            translate.Y=0;
            return;
        }

        var duration=new Duration(TimeSpan.FromMilliseconds(durationMs));
        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        element.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation{From=fromOpacity,To=1,Duration=duration,EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation{From=fromX,To=0,Duration=duration,EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation{From=fromY,To=0,Duration=duration,EasingFunction=easing},HandoffBehavior.SnapshotAndReplace);
    }

    private void ResetBusyMotion()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        BusyOverlay.BeginAnimation(UIElement.OpacityProperty,null);
        BusyOverlay.Opacity=1;
        if(BusyOverlay.RenderTransform is TranslateTransform translate)
        {
            translate.BeginAnimation(TranslateTransform.XProperty,null);
            translate.BeginAnimation(TranslateTransform.YProperty,null);
            translate.X=0;
            translate.Y=0;
        }

        if(BusyDialogCard.RenderTransform is not ScaleTransform scale)return;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);
        scale.ScaleX=1;
        scale.ScaleY=1;
    }

    private static string[] SupportedModDropPaths(IDataObject data)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (!data.GetDataPresent(DataFormats.FileDrop)
            || data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];
        return paths.Where(path => !string.IsNullOrWhiteSpace(path)
            && (Directory.Exists(path) || (File.Exists(path)
                && Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".rar" or ".7z")))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2049).ToArray();
    }

    private void OnWindowPreviewDragOver(object sender, DragEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (e.Data.GetDataPresent(WorkspaceTabPanel.DragFormat, false)) return;
        e.Effects = SupportedModDropPaths(e.Data).Length > 0
            ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowPreviewDrop(object sender, DragEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if (e.Data.GetDataPresent(WorkspaceTabPanel.DragFormat, false)) return;
        e.Handled = true;
        var paths = SupportedModDropPaths(e.Data);
        if (paths.Length == 0 || DataContext is not MainWindowViewModel vm) return;
        try
        {
            await vm.ImportModPathsAsync(paths);
        }
        catch (Exception ex)
        {
            MasterDebugLog.Write("MOD-INTAKE", "Drag-and-drop intake failed.", ex);
            vm.StatusText = "Could not import dropped mods: " + ex.Message;
        }
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

    private bool drainingPageReads;

    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(DataContext is not MainWindowViewModel vm)return;
        if(!vm.CriticalOperation)
        {
            if(drainingPageReads){e.Cancel=true;return;}
            if(!vm.PageReadsInProgress)return;
            e.Cancel=true;
            drainingPageReads=true;
            try
            {
                var pendingReads = vm.StopPageReadsAsync();
                await Dispatcher.Yield(DispatcherPriority.Background);
                await pendingReads;
            }
            catch(Exception ex)
            {
                MasterDebugLog.Write("SHUTDOWN","Could not finish session reads.",ex);
                vm.StatusText="Could not finish closing: "+ex.Message;
                return;
            }
            finally { drainingPageReads=false; }
            Close();
            return;
        }
        e.Cancel=true;
        MessageBox.Show(this,"A transactional or startup operation is still running. Closing now would emulate a process crash, so the normal Close action is blocked until it reaches a commit or rollback point.","Operation in progress",MessageBoxButton.OK,MessageBoxImage.Information);
    }
}
