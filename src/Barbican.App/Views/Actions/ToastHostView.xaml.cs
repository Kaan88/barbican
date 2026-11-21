// Bulwark 1.21.2 reference (default theme): components/ui/toast.tsx:243-261 (360 px, rounded-md, popover fill, border, pt-3 pe-2.5 ps-3.5, status icon 18 px), :545-561 (bottom-5 end-5, gap-2.5),
// globals.css:570-606 (slide in from the end edge; here 150 ms with no overshoot, Bulwark style only). Not built: the countdown bar, pause on hover and swipe (the service owns the timers).
// Mirrors: Bulwark components/ui/toast.tsx (message, action button, close), lib/announce.ts (screen readers hear a toast); UIA RaiseNotificationEvent. Owner actions.

using Barbican.App.Motion;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Actions;
using Microsoft.UI.Xaml.Automation.Peers;

namespace Barbican.App.Views.Actions;

/// <summary>The toast overlay: binds to <see cref="ToastService.Toasts"/> and raises a Narrator notification for each toast shown.</summary>
public sealed partial class ToastHostView : UserControl
{
    public ToastHostView()
    {
        Toasts = ViewHost.Get<ToastService>();
        InitializeComponent();
        Loaded += OnLoaded;
        StyleState.Current.PropertyChanged += (_, _) => Place();
        Place();
        Unloaded += OnUnloaded;
    }

    public ToastService Toasts { get; }

    private bool _subscribed;

    // Bulwark: bottom-right, 360 px; Bento and Nordic keep the bottom-centre stack
    private void Place()
    {
        if (StyleState.Current.IsBulwark)
        {
            Stack.HorizontalAlignment = HorizontalAlignment.Right;
            Stack.Width = 360;
            Stack.Margin = new Thickness(0, 0, 15, 15);
        }
        else
        {
            Stack.HorizontalAlignment = HorizontalAlignment.Center;
            Stack.Width = double.NaN;
            Stack.Margin = new Thickness(0, 0, 0, 23);
        }
    }

    // the card radius follows the style; in Bulwark a toast slides in from the end edge over Motion.Enter (no overshoot, Bulwark has its own 0.32 s one)
    private void OnToastLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border card)
        {
            return;
        }

        card.CornerRadius = StyleState.Current.ToastRadius;
        CardShadow.Apply(card, CardDepth.Large);
        var ms = MotionRuntime.Bulwark ? MotionRuntime.Scale.EnterMs : 0;
        if (ms <= 0)
        {
            return;
        }

        var move = new TranslateTransform { X = 24 };
        card.RenderTransform = move;
        card.Opacity = 0;
        var curve = MotionScale.EnterCurve;
        var board = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        foreach (var (target, property, to) in new (DependencyObject, string, double)[] { (card, "Opacity", 1), (move, "X", 0) })
        {
            var frames = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimationUsingKeyFrames { Duration = MotionRuntime.Ms(ms) };
            frames.KeyFrames.Add(new Microsoft.UI.Xaml.Media.Animation.SplineDoubleKeyFrame
            {
                KeyTime = Microsoft.UI.Xaml.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
                Value = to,
                KeySpline = new Microsoft.UI.Xaml.Media.Animation.KeySpline
                {
                    ControlPoint1 = new global::Windows.Foundation.Point(curve.X1, curve.Y1),
                    ControlPoint2 = new global::Windows.Foundation.Point(curve.X2, curve.Y2),
                },
            });
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(frames, target);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(frames, property);
            board.Children.Add(frames);
        }

        board.Completed += (_, _) =>
        {
            card.Opacity = 1;
            card.RenderTransform = null;
        };
        board.Begin();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        Toasts.Announced += OnAnnounced;
        _subscribed = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        Toasts.Announced -= OnAnnounced;
        _subscribed = false;
    }

    private void OnAnnounced(object? sender, ToastAnnouncedEventArgs e)
    {
        var message = e.Toast.AccessibleName;
        var kind = e.Toast.IsError ? AutomationNotificationKind.ActionAborted : AutomationNotificationKind.ActionCompleted;
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // an alert for an error, a polite notice otherwise; a platform without the event just stays silent
                (FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this))
                    ?.RaiseNotificationEvent(kind, AutomationNotificationProcessing.ImportantMostRecent, message, "toast");
            }
            catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or InvalidOperationException)
            {
                // no UIA notification support here
            }
        });
    }

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the undo reports its own failure with a toast
    private async void OnAction(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: ToastItem item })
        {
            Toasts.Dismiss(item.Handle);
            if (item.Request.Action is { } run)
            {
                await run(CancellationToken.None);
            }
        }
    }

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the action reports its own failure with a toast
    private async void OnSecondary(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: ToastItem item })
        {
            Toasts.Dismiss(item.Handle);
            if (item.Request.SecondaryAction is { } run)
            {
                await run(CancellationToken.None);
            }
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: ToastItem item })
        {
            Toasts.Dismiss(item.Handle);
        }
    }
}
