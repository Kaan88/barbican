// Mirrors: Bulwark components/email/read-receipt-banner.tsx (ask before sending; "Send receipt" / "Ignore"); Rust reader.rs:412 banners (receipt),
// mdn.rs:185 should_offer (requested, not $MDNSent, not a draft, not in Sent/Drafts/Trash/Junk); RFC 8098 §2.1. Never offered on a read-only
// account (R2-14: the write would be refused). Owner reader.

using System.Runtime.CompilerServices;
using Barbican.Core.Mail.Mdn;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Reader;

/// <summary>The read-receipt banner.</summary>
public sealed class ReceiptBannerSource(IMailActions actions, MdnSender sender) : IReaderBannerSource
{
    public const string Id = "receipt";
    public const string Send = "receipt.send";
    public const string Ignore = "receipt.ignore";

    public async IAsyncEnumerable<ReaderBanner> BannersForAsync(ReaderSnapshot snapshot, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await Task.CompletedTask.ConfigureAwait(false);
        if (snapshot.Body is not { } body || !actions.CanWrite || sender.SentThisRun(body.Id))
        {
            yield break;
        }

        var json = EmailJson.Of(body);
        if (!MdnMessage.ShouldOffer(json, snapshot.MailboxRoles, "ask") || MdnMessage.RequestedBy((System.Text.Json.Nodes.JsonArray)json["headers"]!) is not { } to)
        {
            yield break;
        }

        yield return new ReaderBanner(Id, "The sender asked for a read receipt.", $"A receipt goes to {to}.", false, [(Send, "Send receipt"), (Ignore, "Ignore")]);
    }
}
