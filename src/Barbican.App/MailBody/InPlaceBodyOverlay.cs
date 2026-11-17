// Mirrors: docs/adr/0005 decision 1 (Windows: WebView2 composes with XAML overlays, 0 punch-through in S2; the Rust capture machinery is not
// ported). Owner body.

namespace Barbican.App.MailBody;

/// <summary>Windows: overlays draw over the WebView2 like over any element; nothing to do.</summary>
public sealed class InPlaceBodyOverlay : IBodyOverlayStrategy
{
    public void Cover(Window window)
    {
    }

    public void Uncover()
    {
    }
}
