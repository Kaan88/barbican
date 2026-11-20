// Mirrors: Rust main.rs:430 restore_session (one client per login, with its mail and sync hanging off it); M1-plan §4.2 IAccountSession.

using Barbican.Core.Jmap;
using Barbican.Core.Mail;

namespace Barbican.Core.Accounts;

/// <summary>The live objects of one signed-in account.</summary>
public sealed class AccountSession(AccountInfo account, IJmapClient client, IMailService mail, IMailSync sync, IDisposable? tokens,
    bool allowsAddOnStorage = false) : IAccountSession, IDisposable
{
    /// <inheritdoc/>
    public AccountInfo Account { get; } = account;

    /// <inheritdoc/>
    public IJmapClient Client { get; } = client;

    /// <inheritdoc/>
    public IMailService Mail { get; } = mail;

    /// <inheritdoc/>
    public IMailSync Sync { get; } = sync;

    /// <inheritdoc/>
    public bool AllowsAddOnStorage { get; } = allowsAddOnStorage;

    public void Dispose()
    {
        tokens?.Dispose();
        (Sync as IDisposable)?.Dispose();
    }
}
