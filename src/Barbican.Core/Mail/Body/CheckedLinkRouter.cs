// Mirrors: Rust mailview.rs:369-386 (http(s) to the system browser; mailto to the composer, M3); Bulwark lib/plugin-hook-catalog.ts:86
// onBeforeExternalLink (asked before any link leaves the app; false stops it), lib/email-sanitization.ts:261 (mail links open outside).
// Replaces M1's LoggingLinkRouter. A mailto: link waits for the composer (M3) and is not opened. Owner reader.

using Barbican.Core.Common;
using Barbican.Core.Mail.Reader.Hooks;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Core.Mail.Body;

/// <summary>Opens links from messages (and the reader's banners) in the browser after the plugins' link check.</summary>
public sealed class CheckedLinkRouter(IHookBus hooks, Func<Uri, CancellationToken, Task<Result<Unit>>> open) : ILinkRouter
{
    /// <summary>Links opened, and links a plugin stopped (automation counters).</summary>
    public int Opened { get; private set; }

    public int Stopped { get; private set; }

    public void Route(Uri link) => _ = OpenAsync(link, "mail", CancellationToken.None);

    /// <summary>Opens an http(s) link unless a plugin stops it; false when it was not opened.</summary>
    public async Task<bool> OpenAsync(Uri link, string source, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (!link.IsAbsoluteUri || (!string.Equals(link.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal) && !string.Equals(link.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)))
        {
            return false;
        }

        if (hooks.HasHandlers(HookNames.OnBeforeExternalLink)
            && !await hooks.InterceptAsync(HookNames.OnBeforeExternalLink, new ExternalLinkContext(link.AbsoluteUri, source), ct).ConfigureAwait(false))
        {
            Stopped++;
            return false;
        }

        var r = await open(link, ct).ConfigureAwait(false);
        if (r.IsOk)
        {
            Opened++;
        }

        return r.IsOk;
    }
}
