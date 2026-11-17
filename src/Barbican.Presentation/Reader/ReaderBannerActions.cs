// Mirrors: Bulwark components/email/unsubscribe-banner.tsx (one-click POST, else open the link), read-receipt-banner.tsx (send / ignore); Rust
// reader.rs:1168 unsubscribe, mdn.rs:202 send. Every write goes through the account's own client and is refused on a read-only account (R2-14).
// Owner reader.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Body;
using Barbican.Core.Mail.Mdn;
using Barbican.Core.Net;
using Barbican.Presentation.Mail;

namespace Barbican.Presentation.Reader;

/// <summary>Runs a banner button for the message on screen and says how it went.</summary>
public sealed class ReaderBannerActions(ReaderViewModel reader, IActiveMail active, IAccountManager accounts, IGuardedHttp http, MdnSender mdn, CheckedLinkRouter links)
{
    /// <summary>The result line of the last action (automation and the toast).</summary>
    public string? LastResult { get; private set; }

    /// <summary>Runs <paramref name="actionId"/> of the banner shown for <paramref name="emailId"/>; the banner goes once it is done.</summary>
    public async Task<string> RunAsync(string emailId, string actionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        var snapshot = reader.Current;
        if (!string.Equals(snapshot.EmailId, emailId, StringComparison.Ordinal) || snapshot.Body is null)
        {
            return Done("The message is no longer open.");
        }

        return actionId switch
        {
            UnsubscribeBannerSource.OneClick => await OneClickAsync(snapshot, ct).ConfigureAwait(true),
            UnsubscribeBannerSource.OpenLink => await OpenAsync(snapshot, ct).ConfigureAwait(true),
            ReceiptBannerSource.Send => await ReceiptAsync(snapshot, ct).ConfigureAwait(true),
            ReceiptBannerSource.Ignore => Remove(snapshot, ReceiptBannerSource.Id, "No receipt sent."),
            _ => Done("Unknown action."),
        };
    }

    private async Task<string> OneClickAsync(ReaderSnapshot snapshot, CancellationToken ct)
    {
        if (UnsubscribeBannerSource.Links(snapshot) is not ({ Http: { } url }, true))
        {
            return Done("One-click unsubscribe is not offered for this message.");
        }

        var r = await OneClickUnsubscribe.PostAsync(http, url, ct).ConfigureAwait(true);
        return r.IsOk ? Remove(snapshot, UnsubscribeBannerSource.Id, "Unsubscribed.") : Done(r.Error!.Message);
    }

    private async Task<string> OpenAsync(ReaderSnapshot snapshot, CancellationToken ct)
    {
        if (UnsubscribeBannerSource.Links(snapshot) is not ({ Http: { } url }, _) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return Done("No unsubscribe link.");
        }

        return await links.OpenAsync(uri, "unsubscribe", ct).ConfigureAwait(true) ? Done("Opened in the browser.") : Done("The link was not opened.");
    }

    private async Task<string> ReceiptAsync(ReaderSnapshot snapshot, CancellationToken ct)
    {
        if (active.AccountKey is not { } key || accounts.SessionFor(key) is not { } session)
        {
            return Done("No account.");
        }

        var client = session.Client;
        if (MdnSender.UploaderFor(client) is not { } upload)
        {
            return Done("This account cannot upload.");
        }

        var r = await mdn.SendAsync(client, upload, EmailJson.Of(snapshot.Body!), automatic: false, ct).ConfigureAwait(true);
        return r.IsOk ? Remove(snapshot, ReceiptBannerSource.Id, "Receipt sent.") : Done(r.Error!.Message);
    }

    private string Remove(ReaderSnapshot snapshot, string bannerId, string text)
    {
        reader.RemoveBanner(snapshot.EmailId, bannerId);
        return Done(text);
    }

    private string Done(string text)
    {
        LastResult = text;
        return text;
    }
}
