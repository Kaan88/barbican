// Mirrors: PORT-PLAN section 3.7 (one in-window dialog host) and ADR 0007 (every popup is in-window). The shared host lives in the main window's shell overlay; a window of its own (the detached composer,
// Bulwark email-composer.tsx:5121-5131) carries a host over its own DialogService, and this picks the one that belongs to a view's window. Owner composer-ui.

using System.Runtime.CompilerServices;
using Barbican.App.Shell;
using Barbican.Presentation.Dialogs;

namespace Barbican.App.Views.Dialogs;

/// <summary>Finds the dialog service that draws in the window of a view.</summary>
internal static class DialogHostRouter
{
    private static readonly ConditionalWeakTable<XamlRoot, DialogService> Local = new();

    /// <summary>The service of the window <paramref name="owner"/> is in: the window's own host when it carries one, the shared shell host otherwise.</summary>
    public static IDialogService For(FrameworkElement owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return owner.XamlRoot is { } root && Local.TryGetValue(root, out var own) ? own : ViewHost.Get<IDialogService>();
    }

    /// <summary>A window of its own now shows dialogs through <paramref name="service"/>.</summary>
    public static void Register(XamlRoot root, DialogService service)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(service);
        Local.AddOrUpdate(root, service);
    }

    /// <summary>The window is gone or its content moved back to the main window.</summary>
    public static void Unregister(XamlRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        _ = Local.Remove(root);
    }
}
