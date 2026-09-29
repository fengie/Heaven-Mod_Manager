using MhwModManager.Core;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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

    private void OnWindowLoaded(object sender,RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        AnimateElement(RootLayout,0.94,0,6,170);
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
        if(!SystemParameters.ClientAreaAnimation)
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

        if(!SystemParameters.ClientAreaAnimation)
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