using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MhwModManager.App;

public static class UiMotion
{
    public static readonly DependencyProperty EnableHoverFeedbackProperty =
        DependencyProperty.RegisterAttached(
            "EnableHoverFeedback",
            typeof(bool),
            typeof(UiMotion),
            new PropertyMetadata(false, OnEnableHoverFeedbackChanged));

    public static bool GetEnableHoverFeedback(DependencyObject obj) =>
        (bool)obj.GetValue(EnableHoverFeedbackProperty);

    public static void SetEnableHoverFeedback(DependencyObject obj, bool value) =>
        obj.SetValue(EnableHoverFeedbackProperty, value);

    private static void OnEnableHoverFeedbackChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;

        if ((bool)e.OldValue)
        {
            control.MouseEnter -= OnMouseEnter;
            control.MouseLeave -= OnMouseLeave;
            control.PreviewMouseLeftButtonDown -= OnMouseDown;
            control.PreviewMouseLeftButtonUp -= OnMouseUp;
        }

        if (!(bool)e.NewValue) return;

        control.RenderTransformOrigin = new Point(0.5, 0.5);
        control.RenderTransform = new ScaleTransform(1, 1);
        control.MouseEnter += OnMouseEnter;
        control.MouseLeave += OnMouseLeave;
        control.PreviewMouseLeftButtonDown += OnMouseDown;
        control.PreviewMouseLeftButtonUp += OnMouseUp;
    }

    private static void OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Control control) AnimateTo(control, 1.01, 0.96, 110);
    }

    private static void OnMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Control control) AnimateTo(control, 1, 1, 130);
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Control control) AnimateTo(control, 0.985, 0.88, 65);
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Control control) return;
        AnimateTo(control, control.IsMouseOver ? 1.01 : 1, control.IsMouseOver ? 0.96 : 1, 90);
    }

    private static void AnimateTo(Control control, double scale, double opacity, int durationMs)
    {
        if (control.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            control.RenderTransform = transform;
        }

        if (!SystemParameters.ClientAreaAnimation)
        {
            control.BeginAnimation(UIElement.OpacityProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            control.Opacity = opacity;
            transform.ScaleX = scale;
            transform.ScaleY = scale;
            return;
        }

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));

        control.BeginAnimation(
            UIElement.OpacityProperty,
            new DoubleAnimation { To = opacity, Duration = duration, EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation { To = scale, Duration = duration, EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation { To = scale, Duration = duration, EasingFunction = easing },
            HandoffBehavior.SnapshotAndReplace);
    }
}
