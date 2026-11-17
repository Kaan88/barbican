// Mirrors: M1-plan §4.2 (IAccountManager.Active, SessionFor, ActiveChanged). Owner mailcore.

using Barbican.Core.Accounts;
using Barbican.Core.Mail;

namespace Barbican.Presentation.Mail;

/// <summary><see cref="IActiveMail"/> over the account manager.</summary>
public sealed class ActiveMailSource : IActiveMail, IDisposable
{
    private readonly IAccountManager _accounts;

    public ActiveMailSource(IAccountManager accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        _accounts = accounts;
        _accounts.ActiveChanged += OnActiveChanged;
    }

    public event EventHandler? Changed;

    public string? AccountKey => _accounts.Active?.Key;

    public string? AccountUser => _accounts.Active?.User;

    public IMailService? Mail => Session()?.Mail;

    public IMailSync? Sync => Session()?.Sync;

    public void Dispose() => _accounts.ActiveChanged -= OnActiveChanged;

    private IAccountSession? Session() => _accounts.Active is { } a ? _accounts.SessionFor(a.Key) : null;

    private void OnActiveChanged(object? sender, ActiveAccountChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
