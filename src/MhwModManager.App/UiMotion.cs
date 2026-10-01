using MhwModManager.Core;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MhwModManager.App;

public static class UiMotion
{
    public static bool AnimationsEnabled { get; set; } = true;

    private const int HoverInMs = 110;
    private const int HoverOutMs = 130;
    private const int PressMs = 65;
    private const int ReleaseMs = 90;
    private const int PageTransitionMs = 180;

    public static readonly DependencyProperty EnableHoverFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableHoverFeedback",
            typeof(bool),
            typeof(UiMotion),
            new PropertyMetadata(false, OnEnableHoverFeedbackChanged));

    public static readonly DependencyProperty EnableSelectionTransitionProperty =
        DependencyProperty.RegisterAttached(
            "EnableSelectionTransition",
            typeof(bool),
            typeof(UiMotion),
            new PropertyMetadata(false, OnEnableSelectionTransitionChanged));

    public static bool GetEnableHoverFeedback(DependencyObject obj)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (bool)obj.GetValue(EnableHoverFeedbackProperty);
    }

    public static void SetEnableHoverFeedback(DependencyObject obj,bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        obj.SetValue(EnableHoverFeedbackProperty,value);
    }

    public static bool GetEnableSelectionTransition(DependencyObject obj)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        return (bool)obj.GetValue(EnableSelectionTransitionProperty);
    }

    public static void SetEnableSelectionTransition(DependencyObject obj,bool value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        obj.SetValue(EnableSelectionTransitionProperty,value);
    }

    private static void OnEnableHoverFeedbackChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(d is not Control control)return;

        if((bool)e.OldValue)
        {
            control.MouseEnter-=OnMouseEnter;
            control.MouseLeave-=OnMouseLeave;
            control.PreviewMouseLeftButtonDown-=OnMouseDown;
            control.PreviewMouseLeftButtonUp-=OnMouseUp;
        }

        if(!(bool)e.NewValue)return;

        control.RenderTransformOrigin=new Point(0.5,0.5);
        control.RenderTransform=new ScaleTransform(1,1);
        control.MouseEnter+=OnMouseEnter;
        control.MouseLeave+=OnMouseLeave;
        control.PreviewMouseLeftButtonDown+=OnMouseDown;
        control.PreviewMouseLeftButtonUp+=OnMouseUp;
    }

    private static void OnEnableSelectionTransitionChanged(DependencyObject d,DependencyPropertyChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(d is not TabControl tabControl)return;

        if((bool)e.OldValue)
        {
            tabControl.Loaded-=OnTabControlLoaded;
            tabControl.SelectionChanged-=OnTabSelectionChanged;
        }

        if(!(bool)e.NewValue)return;

        tabControl.Loaded+=OnTabControlLoaded;
        tabControl.SelectionChanged+=OnTabSelectionChanged;
    }

    private static void OnTabControlLoaded(object sender,RoutedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(sender is TabControl tabControl)QueueSelectionTransition(tabControl,6);
    }

    private static void OnTabSelectionChanged(object sender,SelectionChangedEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(sender is not TabControl tabControl || !ReferenceEquals(e.Source,tabControl))return;
        QueueSelectionTransition(tabControl,10);
    }

    private static void QueueSelectionTransition(TabControl tabControl,double offset)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        tabControl.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => AnimateSelectedContent(tabControl,offset)));
    }

    private static void AnimateSelectedContent(TabControl tabControl,double offset)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        tabControl.ApplyTemplate();
        if(tabControl.Template.FindName("PART_SelectedContentHost",tabControl) is not ContentPresenter presenter)return;

        presenter.BeginAnimation(UIElement.OpacityProperty,null);

        if(presenter.RenderTransform is not TranslateTransform transform)
        {
            transform=new TranslateTransform();
            presenter.RenderTransform=transform;
        }

        transform.BeginAnimation(TranslateTransform.XProperty,null);
        presenter.Opacity=1;
        transform.X=0;

        if(!AnimationsEnabled||!SystemParameters.ClientAreaAnimation)return;

        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        var duration=new Duration(TimeSpan.FromMilliseconds(PageTransitionMs));

        presenter.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation
            {
                From=0.72,
                To=1,
                Duration=duration,
                EasingFunction=easing,
                FillBehavior=FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation
            {
                From=offset,
                To=0,
                Duration=duration,
                EasingFunction=easing,
                FillBehavior=FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private static void OnMouseEnter(object sender,MouseEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(sender is Control control)AnimateTo(control,1.01,0.96,HoverInMs);
    }

    private static void OnMouseLeave(object sender,MouseEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(sender is Control control)AnimateTo(control,1,1,HoverOutMs);
    }

    private static void OnMouseDown(object sender,MouseButtonEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(sender is Control control)AnimateTo(control,0.985,0.88,PressMs);
    }

    private static void OnMouseUp(object sender,MouseButtonEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(sender is not Control control)return;
        AnimateTo(control,control.IsMouseOver?1.01:1,control.IsMouseOver?0.96:1,ReleaseMs);
    }

    private static void AnimateTo(Control control,double scale,double opacity,int durationMs)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        if(control.RenderTransform is not ScaleTransform transform)
        {
            transform=new ScaleTransform(1,1);
            control.RenderTransform=transform;
        }

        if(!AnimationsEnabled||!SystemParameters.ClientAreaAnimation)
        {
            control.BeginAnimation(UIElement.OpacityProperty,null);
            transform.BeginAnimation(ScaleTransform.ScaleXProperty,null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty,null);
            control.Opacity=opacity;
            transform.ScaleX=scale;
            transform.ScaleY=scale;
            return;
        }

        var easing=new CubicEase{EasingMode=EasingMode.EaseOut};
        var duration=new Duration(TimeSpan.FromMilliseconds(durationMs));

        control.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation{To=opacity,Duration=duration,EasingFunction=easing},
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation{To=scale,Duration=duration,EasingFunction=easing},
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation{To=scale,Duration=duration,EasingFunction=easing},
            HandoffBehavior.SnapshotAndReplace);
    }
}
