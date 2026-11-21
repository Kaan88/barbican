// Mirrors: Bulwark components/layout/edge-drawer.tsx:36-66 (the folders in a drawer from the window's start edge, the page dimmed behind it, a click on the scrim closes it, closed it is
// out of reach: no clicks, no Tab stops), mail-app.tsx:6395-6447 (scrim and drawer below the phone breakpoint), components/layout/navigation-rail.tsx:390-445 (bottom tab bar),
// layout/focus-rescue.tsx:1-30 (focus goes to the list when the element that had it is gone: WCAG 2.4.3), hooks/use-mail-status-announcements.ts:30-62 (a folder that finished loading
// and the search result count are read out once, without moving the focus: WCAG 4.1.3), design/UI-GUIDELINES.md "Nordic Focus" (the sender panel is toggleable).
// WP-10 (SH-11, RS-01, AX-03, AX-04). The drawer slides in over the view token (Bulwark's 300 ms is longer than any token; the exit is instant, design/MOTION.md rule 3).

using Barbican.App.Motion;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace Barbican.App.Shell;

public sealed partial class ShellPage
{
    /// <summary>The caption buttons' height: the one row Bulwark's window leaves for the window's controls.</summary>
    private const double CaptionHeight = 32;

    private bool _drawerShown;
    private bool _focusWasInList;
    private string? _announcedView;
    private DispatcherQueueTimer? _statusTimer;
    private Storyboard? _drawerSlide;

    // Over the sign-in screen the title band takes the colour the backdrop starts with, so the window reads as one surface (login.css: the backdrop is fixed to the whole window)
    private void ApplyBandColour() =>
        TitleBand.Background = SignInRegion.Visibility == Visibility.Visible && SignInRegion.WashColor is { } wash
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(wash)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

    // the tab bar shows with the mail screen's list in the phone layout; the other app screens keep their rail
    private void UpdatePhoneBar() =>
        PhoneBar.Visibility = _phoneBarWanted && MailScreen.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    // The sidebar is a drawer in the phone layout: from the window's start edge over the list, with the scrim behind it; in the desktop layouts it is the column it always was.
    private void ApplyDrawer(bool phone, bool open)
    {
        Grid.SetColumn(SidebarRegion, phone ? 0 : 1);
        Grid.SetColumnSpan(SidebarRegion, phone ? 3 : 1);
        SidebarRegion.HorizontalAlignment = phone ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        AutomationProperties.SetAccessibilityView(Surface, open ? AccessibilityView.Raw : AccessibilityView.Content);
        if (open == _drawerShown)
        {
            return;
        }

        _drawerShown = open;
        if (open)
        {
            DrawerScrim.Show();
            SlideDrawerIn();
            // a pointer that opened it gets no focus ring (focus-visible only for the keyboard); the focus goes into the drawer either way
            var state = CompactFolders.FocusState == FocusState.Pointer ? FocusState.Pointer : FocusState.Programmatic;
            DispatcherQueue.TryEnqueue(() => SidebarRegion.Focus(state));
        }
        else
        {
            DrawerScrim.Hide();
            _drawerSlide?.Stop();
            DrawerShift.X = 0;
            // back to what opened it (focus-rescue.tsx: "back to what was focused before the layer opened")
            DispatcherQueue.TryEnqueue(() =>
            {
                if (CompactFolders.Visibility == Visibility.Visible)
                {
                    CompactFolders.Focus(FocusState.Programmatic);
                }
            });
        }
    }

    private void SlideDrawerIn()
    {
        _drawerSlide?.Stop();
        var scale = MotionRuntime.Scale;
        var ms = scale.Rapid ? 0 : scale.ViewMs;
        if (ms <= 0)
        {
            DrawerShift.X = 0;
            return;
        }

        var width = Math.Max(SidebarRegion.Panes.SidebarWidth > 0 ? SidebarRegion.Panes.SidebarWidth : _look.SidebarWidth, 1);
        var curve = MotionScale.EnterCurve;
        var frames = new DoubleAnimationUsingKeyFrames { Duration = MotionRuntime.Ms(ms) };
        frames.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = -width });
        frames.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
            Value = 0,
            KeySpline = new KeySpline { ControlPoint1 = new Windows.Foundation.Point(curve.X1, curve.Y1), ControlPoint2 = new Windows.Foundation.Point(curve.X2, curve.Y2) },
        });
        Storyboard.SetTarget(frames, DrawerShift);
        Storyboard.SetTargetProperty(frames, "X");
        var board = new Storyboard { FillBehavior = FillBehavior.Stop };
        board.Children.Add(frames);
        _drawerSlide = board;
        board.Begin();
    }

    private void OnDrawerScrimTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseCompactFolders();
        e.Handled = true;
    }

    // Nordic Focus: the sender panel shows when the window has room for it and the user has not hidden it; the toggle is there whenever the panel could show, so hiding it is reversible.
    // The Bento panel is not toggleable (it is one of its tiles); Bulwark has none.
    private void ApplyContext(ShellLayout layout)
    {
        var available = layout.ContextShown && !_composing;
        var toggleable = available && _look.IsNordic;
        ContextRegion.Visibility = available && !(toggleable && _contextHidden) ? Visibility.Visible : Visibility.Collapsed;
        ContextToggle.Visibility = toggleable ? Visibility.Visible : Visibility.Collapsed;
        ContextToggle.IsChecked = !_contextHidden;
        var label = _contextHidden ? "Show sender panel" : "Hide sender panel";
        Tip.SetText(ContextToggle, label);
        AutomationProperties.SetHelpText(ContextToggle, label);
    }

    private void OnContextToggle(object sender, RoutedEventArgs e)
    {
        _contextHidden = ContextToggle.IsChecked != true;
        ApplyLook();
    }

    // AX-03 / AX-04: focus rescue for the list and a status line for the folder that finished loading.
    private void WireAccessibility()
    {
        // GotFocus bubbles: the page hears every element that takes the focus
        GotFocus += (_, e) => _focusWasInList = e.OriginalSource is DependencyObject source && IsInside(source, ListRegion);
        ListRegion.List.Rows.CollectionChanged += (_, _) => RescueListFocus();
        ListRegion.List.PropertyChanged += OnListStatusChanged;
    }

    // The focused row was deleted or moved away: the focus would fall to the page and a keyboard user would start again from the top. It goes to the list (the row that took its place)
    // when, and only when, nothing else has it (focus-rescue.tsx: "It never moves a focus the user put somewhere else").
    private void RescueListFocus()
    {
        if (!_focusWasInList)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (XamlRoot is null || SignInRegion.Visibility == Visibility.Visible)
            {
                return;
            }

            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            var lost = focused is null || ReferenceEquals(focused, this) || !IsInside(focused, Root);
            if (lost && ListColumn.Visibility == Visibility.Visible)
            {
                ListRegion.Focus(FocusState.Programmatic);
            }
        });
    }

    // "{folder}: {count} messages" once per folder after the last page has loaded (a debounce of 600 ms so a folder that loads in steps is read once), and the number of results of a search.
    private void OnListStatusChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MessageListViewModel.IsLoading), StringComparison.Ordinal) || ListRegion.List.IsLoading || ListRegion.List.Error is not null)
        {
            return;
        }

        var list = ListRegion.List;
        var searching = !string.IsNullOrWhiteSpace(list.SearchText);
        var view = $"{list.MailboxId}\u0000{list.Title}\u0000{searching}\u0000{(searching ? list.SearchText : string.Empty)}";
        if (string.Equals(view, _announcedView, StringComparison.Ordinal))
        {
            return;
        }

        _announcedView = view;
        var count = list.Total;
        var noun = count == 1 ? "message" : "messages";
        var text = searching
            ? count == 0 ? "No results" : count == 1 ? "1 result" : $"{count} results"
            : string.IsNullOrEmpty(list.Title) ? string.Empty : count == 0 ? $"{list.Title}: no messages" : $"{list.Title}: {count} {noun}";
        if (text.Length == 0)
        {
            return;
        }

        _statusTimer ??= DispatcherQueue.CreateTimer();
        _statusTimer.Stop();
        _statusTimer.Interval = TimeSpan.FromMilliseconds(600);
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick -= OnStatusTick;
        _statusTimer.Tick += OnStatusTick;
        _pendingStatus = text;
        _statusTimer.Start();
    }

    private string _pendingStatus = string.Empty;

    private void OnStatusTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        try
        {
            // a polite notice on the list; a platform without UIA notifications stays silent
            (FrameworkElementAutomationPeer.FromElement(ListRegion) ?? FrameworkElementAutomationPeer.CreatePeerForElement(ListRegion))
                ?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, _pendingStatus, "list-status");
        }
        catch (Exception ex) when (ex is NotImplementedException or NotSupportedException or InvalidOperationException)
        {
            // no UIA notification support here
        }
    }
}
