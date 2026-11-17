// Mirrors: Rust mailview.rs:369-386 (http(s) to the system browser, mailto to Barbican's composer) and B-COMP-034 (a mailto link clicked in a message opens the composer, never the OS handler); Bulwark lib/protocol-handlers/mailto.ts.
// Wraps the reader's CheckedLinkRouter (links and banners keep their plugin link check): a mailto: link goes to the MailtoRouter, everything else to the wrapped router. Owner composer-ui.

using Barbican.Core.Mail.Body;
using Barbican.Presentation.Compose;

namespace Barbican.App.Views.Compose;

/// <summary>Sends mailto links from messages to the composer.</summary>
public sealed class ComposerLinkRouter(ILinkRouter inner, MailtoRouter mailto) : ILinkRouter
{
    /// <inheritdoc />
    public void Route(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.IsAbsoluteUri && string.Equals(link.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase))
        {
            _ = mailto.HandleAsync(link.OriginalString, CancellationToken.None);
            return;
        }

        inner.Route(link);
    }
}
