// Mirrors: Bulwark components/email/unsubscribe-banner.tsx (List-Unsubscribe banner, one-click first); Rust reader.rs:412 banners (unsub), :1168 unsubscribe
// (one-click only with the server's DKIM pass for the From domain, else the link opens in the browser; mailto shown as the address, the composer is M3);
// RFC 2369, RFC 8058. Owner reader.

using System.Runtime.CompilerServices;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Reader;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Reader;

/// <summary>The mailing-list banner.</summary>
public sealed class UnsubscribeBannerSource : IReaderBannerSource
{
    public const string Id = "unsubscribe";
    public const string OneClick = "unsubscribe.one-click";
    public const string OpenLink = "unsubscribe.open";

    /// <summary>The links of a message, and whether one-click may be used (RFC 8058 needs a DKIM signature covering the headers: the server's pass).</summary>
    public static (UnsubscribeLinks Links, bool OneClickAllowed)? Links(ReaderSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Body is not { } body || snapshot.MailboxRoles.Any(r => r is "sent" or "drafts")
            || Header(body.Headers, "List-Unsubscribe") is not { } list
            || UnsubscribeParser.Parse(list, Header(body.Headers, "List-Unsubscribe-Post")) is not { } links)
        {
            return null;
        }

        var from = body.From.Count > 0 ? body.From[0].Email : string.Empty;
        return (links, links.OneClick && links.Http is not null && DkimGate.ServerDkimPass(snapshot.ServerResults, from));
    }

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        if (Links(snapshot) is not var (links, oneClick))
        {
            yield break;
        }

        List<(string, string)> actions = [];
        if (oneClick)
        {
            actions.Add((OneClick, "Unsubscribe"));
        }
        else if (links.Http is not null)
        {
            actions.Add((OpenLink, "Unsubscribe in browser"));
        }

        var detail = links.Http is null && links.Mailto is not null
            ? $"To unsubscribe, send an e-mail to {UnsubscribeParser.ParseMailto(links.Mailto)?.To ?? links.Mailto}."
            : null;
        yield return new ReaderBanner(Id, "This message is from a mailing list.", detail, false, actions);
    }

    // the first header of that name, unfolded (Rust reader.rs:27 header): a List-Unsubscribe folded inside a URL still parses
    private static string? Header(IReadOnlyList<Barbican.Core.Mail.Model.EmailHeader> headers, string name) => ReaderHeaders.Header(headers, name);
}
