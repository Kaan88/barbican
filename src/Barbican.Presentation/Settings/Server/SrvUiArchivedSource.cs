// Mirrors: Bulwark lib/jmap/client.ts:13411-13500 (queryArchivedItemIds, restoreArchivedItems), Core ArchivedItems; Rust server_settings.rs:137-152. The real source of the Recover page: the active account's client. Calls complete off the UI thread (the JMAP client). Owner srv-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Archive;

namespace Barbican.Presentation.Settings.Server;

/// <summary>Reads and restores archived items over the active account.</summary>
public sealed class SrvUiArchivedSource : ISrvUiRecoverSource
{
    private const int Limit = 200;
    private readonly IAccountManager _accounts;

    public SrvUiArchivedSource(IAccountManager accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        _accounts = accounts;
    }

    public bool Available => Client() is { } c && ArchivedItems.Advertised(c);

    public Task<Result<IReadOnlyList<ArchivedItem>>> ListAsync(CancellationToken ct) =>
        Client() is { } c
            ? ArchivedItems.ListAsync(c, Limit, ct)
            : Task.FromResult(Result<IReadOnlyList<ArchivedItem>>.Fail(new ValidationError("Sign in to an account first.")));

    public Task<Result<Unit>> RestoreAsync(string id, CancellationToken ct) =>
        Client() is { } c
            ? ArchivedItems.RestoreAsync(c, id, ct)
            : Task.FromResult(Result<Unit>.Fail(new ValidationError("Sign in to an account first.")));

    private Barbican.Core.Jmap.IJmapClient? Client() => _accounts.Active is { } a ? _accounts.SessionFor(a.Key)?.Client : null;
}
