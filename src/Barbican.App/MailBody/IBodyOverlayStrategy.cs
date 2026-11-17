// Mirrors: Rust mailview.rs freeze/set_shown/FRESH/HIDE_PENDING (tag rust-final: the capture shown in the WebView's place under an overlay);
// PORT-PLAN.md §3.6; docs/adr/0005 decisions 1 and 3 (Windows composes WebView2 with XAML: no stand-in; Linux: Uno hides the WebKit window
// under a full-window overlay, so a captured stand-in keeps the message visible under dialogs and scrims; menus need none). Owner body.

namespace Barbican.App.MailBody;

/// <summary>What the body view does while a dialog or a full-window scrim covers it.</summary>
public interface IBodyOverlayStrategy
{
    /// <summary>A dialog or scrim is about to cover the body (call before it shows).</summary>
    void Cover(Window window);

    /// <summary>The cover is gone.</summary>
    void Uncover();
}
