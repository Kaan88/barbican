// Mirrors: Rust unified.rs:474 shared_apply and mail.rs apply (an action on unified rows goes to each row's own account). M2 plan 6.5.
// Owner unified. Wraps the actions stream's writer source: the writer it hands out sends row ids of other accounts (and shared accounts) to
// their own writer, with folders matched by role, else name.

using Barbican.Core.Mail.Unified;
using Barbican.Core.Mail.Write;
using Barbican.Presentation.Actions;

namespace Barbican.Presentation.Unified;

/// <summary>The writer source of the app with unified rows routed to their own account.</summary>
public sealed class UnifiedMailWriterSource(IMailWriterSource inner, IUnifiedLogins logins) : IMailWriterSource
{
    /// <inheritdoc />
    public IMailWriter? WriterOf(string accountKey) =>
        inner.WriterOf(accountKey) is { } own ? new UnifiedMailWriter(own, Source, MapAsync) : null;

    /// <inheritdoc />
    public bool CanWrite(string accountKey) => inner.CanWrite(accountKey);

    private MailWriter? Source(UnifiedRowId row) =>
        logins.ClientOf(row.Login) is { IsReadOnly: false } client ? new MailWriter(client, row.SharedAccount ?? client.Session.MailAccountId) : null;

    // a folder of the account on screen, found in the row's account by role, else by name (Rust match_mailbox)
    private async Task<string?> MapAsync(UnifiedRowId row, string mailboxId, CancellationToken ct)
    {
        var active = logins.Active;
        if (string.Equals(row.Login, active, StringComparison.Ordinal) && row.SharedAccount is null)
        {
            return mailboxId;
        }

        if (active is null || logins.ClientOf(active) is not { } here || logins.ClientOf(row.Login) is not { } there)
        {
            return null;
        }

        var picked = await UnifiedFetcher.MailboxesAsync(new UnifiedSource(active, here, here.Session.MailAccountId, false), ct).ConfigureAwait(false);
        var target = await UnifiedFetcher.MailboxesAsync(new UnifiedSource(row.Login, there, row.SharedAccount ?? there.Session.MailAccountId, row.SharedAccount is not null), ct).ConfigureAwait(false);
        if (!picked.IsOk || !target.IsOk || picked.Value!.FirstOrDefault(m => string.Equals((string?)m["id"], mailboxId, StringComparison.Ordinal)) is not { } box)
        {
            return null;
        }

        return UnifiedRows.MatchMailbox(target.Value!, (string?)box["role"] ?? "", (string?)box["name"] ?? "");
    }
}
