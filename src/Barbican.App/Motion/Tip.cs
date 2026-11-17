// Mirrors: design/MOTION.md "Building blocks": Tip waits Motion.tip-delay, then fades in over Motion.enter, removed instantly when the pointer leaves;
// docs/adr/0007 (ToolTip creates a windowed popup that can leave the window, so ToolTipService.ToolTip is denied in App XAML: PopupControls.Denied).

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Motion;

/// <summary>An in-window tooltip. <c>motion:Tip.Text="Mail"</c> on any element shows the text in the shell's overlay after the tip delay.</summary>
public static partial class Tip
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Tip), new PropertyMetadata(null, OnTextChanged));

    private static Canvas? _host;
    private static DispatcherQueueTimer? _timer;
    private static FrameworkElement? _target;
    private static Border? _bubble;
    private static TextBlock? _label;

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    /// <summary>The shell's overlay canvas (ShellPage sets it); without one a tip simply never shows.</summary>
    public static void SetHost(Canvas host) => _host = host;

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.PointerEntered -= OnEntered;
        element.PointerExited -= OnLeft;
        element.PointerPressed -= OnLeft;
        element.PointerCanceled -= OnLeft;
        element.Unloaded -= OnUnloaded;
        if (e.NewValue is string { Length: > 0 })
        {
            element.PointerEntered += OnEntered;
            element.PointerExited += OnLeft;
            element.PointerPressed += OnLeft;
            element.PointerCanceled += OnLeft;
            element.Unloaded += OnUnloaded;
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e) => Hide();

    private static void OnEntered(object sender, PointerRoutedEventArgs e)
    {
        Hide();
        _target = (FrameworkElement)sender;
        _timer ??= _target.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(MotionScale.TipDelayMs);
        _timer.IsRepeating = false;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private static void OnLeft(object sender, PointerRoutedEventArgs e) => Hide();

    private static void OnTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (_target is { } target && _host is { } host && GetText(target) is { Length: > 0 } text)
        {
            Show(host, target, text);
        }
    }

    private static void Show(Canvas host, FrameworkElement target, string text)
    {
        if (_bubble is null)
        {
            _label = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["Text"] };
            _bubble = new Border
            {
                Background = (Brush)Application.Current.Resources["Popup"],
                BorderBrush = (Brush)Application.Current.Resources["Line"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(9, 4, 9, 5),
                IsHitTestVisible = false,
                Child = _label,
            };
        }

        _label!.Text = text;
        if (!host.Children.Contains(_bubble))
        {
            host.Children.Add(_bubble);
        }

        _bubble.Measure(new Windows.Foundation.Size(host.ActualWidth, host.ActualHeight));
        var at = target.TransformToVisual(host).TransformPoint(new Windows.Foundation.Point(0, 0));
        var size = _bubble.DesiredSize;
        // beside a rail button (left edge), else under the element
        var onRail = at.X < 100 && target.ActualHeight >= 30;
        var x = onRail ? at.X + target.ActualWidth + 8 : at.X + (target.ActualWidth - size.Width) / 2;
        var y = onRail ? at.Y + (target.ActualHeight - size.Height) / 2 : at.Y + target.ActualHeight + 6;
        Canvas.SetLeft(_bubble, Math.Clamp(x, 4, Math.Max(4, host.ActualWidth - size.Width - 4)));
        Canvas.SetTop(_bubble, Math.Clamp(y, 4, Math.Max(4, host.ActualHeight - size.Height - 4)));
        _bubble.Visibility = Visibility.Visible;
        MotionRuntime.FadeIn(_bubble);
    }

    private static void Hide()
    {
        _timer?.Stop();
        if (_bubble is not null)
        {
            _bubble.Visibility = Visibility.Collapsed;
        }
    }
}
