// Mirrors: M1-plan §4.2 (IAccountManager.Active, SessionFor, ActiveChanged), §4.3 (UI-thread marshalling). Owner mailcore.
// M1 hotfix (sign-in): the account manager raises ActiveChanged on whatever thread finished the sign-in or the restore (a thread-pool
// continuation after the OAuth loopback, the start-up task); Changed reaches ViewModels bound to the UI (the message list's rows, the rail's
// labels, MailShellStartup's loads that continue on the UI context). Raised there, it ended HAND TEST 1 with RPC_E_WRONG_THREAD and, on the
// restart, left an empty mail screen. Changed is now raised on the UI thread only.

using Barbican.Core.Accounts;
using Barbican.Core.Mail;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Mail;

/// <summary><see cref="IActiveMail"/> over the account manager.</summary>
public sealed class ActiveMailSource : IActiveMail, IDisposable
{
    private readonly IAccountManager _accounts;
    private readonly IDispatcher _dispatcher;

    public ActiveMailSource(IAccountManager accounts, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _accounts = accounts;
        _dispatcher = dispatcher;
        _accounts.ActiveChanged += OnActiveChanged;
    }

    /// <summary>The active account changed; always raised on the UI thread.</summary>
    public event EventHandler? Changed;

    public string? AccountKey => _accounts.Active?.Key;

    public string? AccountUser => _accounts.Active?.User;

    public IMailService? Mail => Session()?.Mail;

    public IMailSync? Sync => Session()?.Sync;

    public void Dispose() => _accounts.ActiveChanged -= OnActiveChanged;

    /// <inheritdoc/>
    public (string? AccountKey, IMailSync? Sync) SyncSnapshot()
    {
        var key = _accounts.Active?.Key;
        return (key, key is null ? null : _accounts.SessionFor(key)?.Sync);
    }

    private IAccountSession? Session() => _accounts.Active is { } a ? _accounts.SessionFor(a.Key) : null;

    private void OnActiveChanged(object? sender, ActiveAccountChangedEventArgs e)
    {
        if (_dispatcher.HasThreadAccess)
        {
            Raise();
        }
        else
        {
            // the account manager's work is not held up by the UI (and cannot fail because of it); the subscribers read Active when it runs
            _dispatcher.Post(Raise);
        }
    }

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
