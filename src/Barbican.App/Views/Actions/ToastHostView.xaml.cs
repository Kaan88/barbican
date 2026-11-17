// Mirrors: Bulwark components/ui/toast.tsx (message, action button, close), lib/announce.ts (screen readers hear a toast); UIA RaiseNotificationEvent. Owner actions.

using Barbican.App.Shell;
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
        Toasts.Announced += OnAnnounced;
        Unloaded += (_, _) => Toasts.Announced -= OnAnnounced;
    }

    public ToastService Toasts { get; }

    private void OnAnnounced(object? sender, ToastAnnouncedEventArgs e)
    {
        var message = e.Toast.Message;
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
