using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using MhwModManager.Core;

namespace MhwModManager.App;

/// <summary>Orders header visuals only, preserving the TabControl Items and stable command indices.</summary>
public sealed class WorkspaceTabPanel : Panel
{
    public const string DragFormat = "Heaven.ModManager.WorkspaceTab.v1";
    private IReadOnlyList<string> order;
    private TabItem? pressedTab;
    private TabItem? draggedTab;
    private Point pressPoint;

    public WorkspaceTabPanel()
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        order = WorkspaceLayout.Default.NavigationOrder;
        AllowDrop = true;
    }

    public IReadOnlyList<string> Order
    {
        get
        {
            using var __mhwTrace = MasterDebugLog.BeginMethod();
            return order;
        }
    }
    public event EventHandler? OrderChanged;

    public void SetOrder(IEnumerable<string>? value)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        order = WorkspaceLayout.NormalizeOrder(value);
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var height = 0d;
        var width = 0d;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            height += child.DesiredSize.Height;
            width = Math.Max(width, child.DesiredSize.Width);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var ranks = order.Select((id, index) => (id, index))
            .ToDictionary(item => item.id, item => item.index, StringComparer.Ordinal);
        var children = InternalChildren.Cast<UIElement>()
            .OrderBy(child => child is TabItem tab && ranks.TryGetValue(AutomationProperties.GetAutomationId(tab), out var rank) ? rank : int.MaxValue);
        var y = 0d;
        var index = 0;
        foreach (var child in children)
        {
            if (KeyboardNavigation.GetTabIndex(child) != index)
                KeyboardNavigation.SetTabIndex(child, index);
            index++;
            child.Arrange(new Rect(0, y, finalSize.Width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
        return finalSize;
    }

    private TabItem? HeaderAt(DependencyObject? node)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        while (node is not null && !ReferenceEquals(node, this))
        {
            if (node is TabItem tab && InternalChildren.Contains(tab)) return tab;
            node = node is Visual or Visual3D
                ? VisualTreeHelper.GetParent(node)
                : (node as FrameworkContentElement)?.Parent;
        }
        return null;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        base.OnPreviewMouseLeftButtonDown(e);
        pressedTab = HeaderAt(e.OriginalSource as DependencyObject);
        pressPoint = e.GetPosition(this);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        pressedTab = null;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        base.OnPreviewMouseMove(e);
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            pressedTab = null;
            return;
        }
        if (pressedTab is null || draggedTab is not null || !order.Contains(AutomationProperties.GetAutomationId(pressedTab), StringComparer.Ordinal)) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - pressPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - pressPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        draggedTab = pressedTab;
        pressedTab = null;
        try
        {
            _ = DragDrop.DoDragDrop(this, new DataObject(DragFormat, AutomationProperties.GetAutomationId(draggedTab)), DragDropEffects.Move);
        }
        finally { draggedTab = null; }
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        base.OnDragOver(e);
        if (!e.Data.GetDataPresent(DragFormat, false)) return;
        e.Effects = draggedTab is not null &&
            HeaderAt(e.OriginalSource as DependencyObject) is TabItem target &&
            !ReferenceEquals(draggedTab, target) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        base.OnDrop(e);
        if (!e.Data.GetDataPresent(DragFormat, false)) return;
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (draggedTab is null || HeaderAt(e.OriginalSource as DependencyObject) is not TabItem target ||
            ReferenceEquals(draggedTab, target)) return;
        MoveHeader(AutomationProperties.GetAutomationId(draggedTab), AutomationProperties.GetAutomationId(target), e.GetPosition(target).Y >= target.ActualHeight / 2);
        e.Effects = DragDropEffects.Move;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        base.OnPreviewKeyDown(e);
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) ||
            e.Key is not (Key.Up or Key.Down) ||
            HeaderAt(e.OriginalSource as DependencyObject) is not TabItem tab) return;
        var current = order.ToList().IndexOf(AutomationProperties.GetAutomationId(tab));
        var next = current + (e.Key == Key.Up ? -1 : 1);
        if (current < 0 || next < 0 || next >= order.Count) return;
        MoveHeader(AutomationProperties.GetAutomationId(tab), order[next], e.Key == Key.Down);
        e.Handled = true;
    }

    private void MoveHeader(string source, string target, bool after)
    {
        using var __mhwTrace = MasterDebugLog.BeginMethod();
        var updated = WorkspaceLayout.Reorder(order, source, target, after);
        if (order.SequenceEqual(updated, StringComparer.Ordinal)) return;
        SetOrder(updated);
        OrderChanged?.Invoke(this, EventArgs.Empty);
    }
}
