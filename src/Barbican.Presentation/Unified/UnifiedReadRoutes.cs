// Mirrors: Rust main.rs:426-427 (mail::owner, mail::shared_account: a unified row is read through its own login and account; tag rust-final);
// Bulwark lib/unified-mailbox.ts (every unified email keeps its accountId). Fable review 3 R3-01: before this, every row of "All inboxes"
// was read from the account on screen with its composite id, and none could be opened. Owner unified.
// A row of the account on screen's own mail account reads through that account's service; any other row through a service of its own
// login and JMAP account (the shared one for a shared row), kept per login and account while the login's client stays the same.

using System.Collections.Concurrent;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Unified;

/// <summary>Routes the reader's reads of unified rows to the account that holds them.</summary>
public sealed class UnifiedReadRoutes(IUnifiedLogins logins, TimeProvider time) : IReadRoutes
{
    private readonly ConcurrentDictionary<string, (IJmapClient Client, ReadRoute Route)> _routes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Result<ReadRoute>? Route(string accountKey, IMailService mail, string rowId)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (UnifiedRows.ParseRow(rowId) is not { } row)
        {
            return null;
        }

        if (string.Equals(row.Login, accountKey, StringComparison.Ordinal) && row.SharedAccount is null)
        {
            return Result<ReadRoute>.Ok(new ReadRoute(mail, null));
        }

        if (logins.ClientOf(row.Login) is not { } client)
        {
            return Result<ReadRoute>.Fail(new ValidationError("The account of this message is not connected."));
        }

        var accountId = row.SharedAccount ?? client.Session.MailAccountId;
        var key = UnifiedRows.SourceKey(row.Login, row.SharedAccount);
        var entry = _routes.AddOrUpdate(
            key,
            _ => (client, Make(client, accountId)),
            (_, old) => ReferenceEquals(old.Client, client) ? old : (client, Make(client, accountId)));
        return Result<ReadRoute>.Ok(entry.Route);
    }

    private ReadRoute Make(IJmapClient client, string accountId) =>
        new(new MailService(client, time, accountId), new ClientBlobSource(client, accountId));
}
