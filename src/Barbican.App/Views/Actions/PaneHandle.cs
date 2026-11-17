// Mirrors: Bulwark components/layout/resize-handle.tsx (drag resizes, arrow keys nudge, double-click resets), Rust dnd.rs pane handles; spec I-071, B-DND-009; hook onColumnResize.
// Owner actions.

using Barbican.App.Shell;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Actions;

/// <summary>
/// A thin draggable handle on the right edge of a pane. It only tells <see cref="PanesViewModel"/> the new width: nothing animates, and the file is written when the drag ends.
/// The drag is the control's own pointer capture (no input is injected anywhere); the same widths are set in automation with <c>pane-set</c>.
/// </summary>
public sealed class PaneHandle : ContentControl
{
    private readonly PanesViewModel _panes;
    private double _startX;
    private double _startWidth;
    private bool _dragging;

    public PaneHandle()
    {
        _panes = ViewHost.Get<PanesViewModel>();
        Width = 8;
        HorizontalAlignment = HorizontalAlignment.Right;
        Margin = new Thickness(0, 0, -4, 0);
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        IsTabStop = true;
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => _dragging = false;
        DoubleTapped += (_, _) => _panes.Reset(Column);
        KeyDown += OnKeyDown;
        AutomationProperties.SetName(this, "Resize pane");
    }

    /// <summary>The hook's column name: sidebar or email-list.</summary>
    public string Column { get; set; } = "sidebar";

    /// <summary>The pane whose width the handle changes.</summary>
    public FrameworkElement? Target { get; set; }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = CapturePointer(e.Pointer);
        _startX = e.GetCurrentPoint(null).Position.X;
        _startWidth = Target?.ActualWidth ?? 0;
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _panes.Resize(Column, (float)(_startWidth + (e.GetCurrentPoint(null).Position.X - _startX)));
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleasePointerCapture(e.Pointer);
            _panes.Commit(Column);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var direction = e.Key switch
        {
            global::Windows.System.VirtualKey.Left => -1,
            global::Windows.System.VirtualKey.Right => 1,
            _ => 0,
        };
        if (direction == 0)
        {
            return;
        }

        e.Handled = true;
        _panes.Nudge(Column, (float)(Target?.ActualWidth ?? 0), direction);
        _panes.Commit(Column);
    }
}
