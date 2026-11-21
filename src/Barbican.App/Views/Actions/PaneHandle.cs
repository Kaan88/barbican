// Mirrors: Bulwark components/layout/resize-handle.tsx (drag resizes, arrow keys nudge, double-click resets; :148-175 role=separator with aria-valuenow/min/max;
// :172 a 4 px line, hover:bg-primary/30, active:bg-primary/50, focus-visible:bg-primary/40, col-resize cursor, no transition-colors animation beyond the colour; :122-131 a 12 px hit area),
// Rust dnd.rs pane handles; spec I-071, B-DND-009; hook onColumnResize. Owner actions. WP-10 (SH-13).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Shell;
using Barbican.Core.Layout;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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
    private double _startY;
    private double _startWidth;
    private bool _horizontal;
    private bool _dragging;
    private bool _over;
    private bool _focused;
    private readonly Border _line = new() { Width = 4, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false };

    public PaneHandle()
    {
        _panes = ViewHost.Get<PanesViewModel>();
        // a 12 px hit area centred over the seam with a 4 px line (resize-handle.tsx:122-131, 172): the panes stay flush
        Width = 12;
        HorizontalAlignment = HorizontalAlignment.Right;
        Margin = new Thickness(0, 0, -6, 0);
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        Content = _line;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        IsTabStop = true;
        UseSystemFocusVisuals = false;
        PointerEntered += (_, _) => { _over = true; Tint(); };
        PointerExited += (_, _) => { _over = false; Tint(); };
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCaptureLost += (_, _) => { _dragging = false; Tint(); };
        GotFocus += (_, _) => { _focused = true; Tint(); };
        LostFocus += (_, _) => { _focused = false; Tint(); };
        DoubleTapped += (_, _) => _panes.Reset(Column);
        KeyDown += OnKeyDown;
        AutomationProperties.SetName(this, "Resize pane");
#if WINDOWS
        ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
#endif
    }

    // hover 30 %, focus 40 %, pressed 50 % of the primary colour, nothing at rest; the tint switches at once (no animation: Bulwark's transition-colors is the 150 ms system colour step)
    private void Tint()
    {
        var alpha = _dragging ? 0.5 : _focused ? 0.4 : _over ? 0.3 : 0.0;
        var accent = AppResources.Brush("Accent").Color;
        _line.Background = new SolidColorBrush(Windows.UI.Color.FromArgb((byte)Math.Round(255 * alpha), accent.R, accent.G, accent.B));
    }

    /// <summary>The pane's width on screen, and the limits of it, for a screen reader (resize-handle.tsx:166-168 aria-valuenow/min/max).</summary>
    internal double CurrentWidth => (_horizontal ? Target?.ActualHeight : Target?.ActualWidth) ?? 0;

    internal double Minimum => _horizontal ? PaneLimits.ListHeightMin : string.Equals(Column, "sidebar", StringComparison.Ordinal) ? PaneLimits.SideMin : PaneLimits.ListMin;

    internal double Maximum => _horizontal ? PaneLimits.ListHeightMax : string.Equals(Column, "sidebar", StringComparison.Ordinal) ? PaneLimits.SideMax : PaneLimits.ListMax;

    /// <summary>The handle of the stacked mail layout: a bar along the bottom edge of the list that resizes its height (resize-handle.tsx orientation="horizontal"); the hook column is <see cref="PanesViewModel.ListHeightColumn"/>.</summary>
    public bool Horizontal
    {
        get => _horizontal;
        set
        {
            if (_horizontal == value) return;
            _horizontal = value;
            if (value)
            {
                Width = double.NaN;
                Height = 12;
                HorizontalAlignment = HorizontalAlignment.Stretch;
                VerticalAlignment = VerticalAlignment.Bottom;
                Margin = new Thickness(0, 0, 0, -6);
                _line.Width = double.NaN;
                _line.Height = 4;
                _line.HorizontalAlignment = HorizontalAlignment.Stretch;
                _line.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                Width = 12;
                Height = double.NaN;
                HorizontalAlignment = HorizontalAlignment.Right;
                VerticalAlignment = VerticalAlignment.Stretch;
                Margin = new Thickness(0, 0, -6, 0);
                _line.Width = 4;
                _line.Height = double.NaN;
                _line.HorizontalAlignment = HorizontalAlignment.Center;
                _line.VerticalAlignment = VerticalAlignment.Stretch;
            }
#if WINDOWS
            ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(value ? Microsoft.UI.Input.InputSystemCursorShape.SizeNorthSouth : Microsoft.UI.Input.InputSystemCursorShape.SizeWestEast);
#endif
        }
    }

    internal void ApplyWidth(double width)
    {
        _panes.Resize(Column, (float)width);
        _panes.Commit(Column);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new PaneHandlePeer(this);

    /// <summary>The hook's column name: sidebar or email-list.</summary>
    public string Column { get; set; } = "sidebar";

    /// <summary>The pane whose width the handle changes.</summary>
    public FrameworkElement? Target { get; set; }

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        _dragging = CapturePointer(e.Pointer);
        Tint();
        _startX = e.GetCurrentPoint(null).Position.X;
        _startY = e.GetCurrentPoint(null).Position.Y;
        _startWidth = CurrentWidth;
        e.Handled = true;
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            var at = e.GetCurrentPoint(null).Position;
            _panes.Resize(Column, (float)(_startWidth + (_horizontal ? at.Y - _startY : at.X - _startX)));
        }
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            Tint();
            ReleasePointerCapture(e.Pointer);
            _panes.Commit(Column);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var direction = e.Key switch
        {
            global::Windows.System.VirtualKey.Left when !_horizontal => -1,
            global::Windows.System.VirtualKey.Right when !_horizontal => 1,
            global::Windows.System.VirtualKey.Up when _horizontal => -1,
            global::Windows.System.VirtualKey.Down when _horizontal => 1,
            _ => 0,
        };
        if (direction == 0)
        {
            return;
        }

        e.Handled = true;
        _panes.Nudge(Column, (float)CurrentWidth, direction);
        _panes.Commit(Column);
    }
}
