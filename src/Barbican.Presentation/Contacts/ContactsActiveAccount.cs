// Mirrors: M5 plan section 4.12 (Contacts is in the rail while the login has the contacts capability), Bulwark components/layout/navigation-rail.tsx. Owner con-ui.

using Barbican.Core.Accounts;
using Barbican.Core.Contacts;

namespace Barbican.Presentation.Contacts;

/// <summary>The default <see cref="IContactsAccountSource"/>: the active account, and whether its session advertises the contacts capability.</summary>
public sealed class ContactsActiveAccount : IContactsAccountSource, IDisposable
{
    private readonly IAccountManager _accounts;

    public ContactsActiveAccount(IAccountManager accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        _accounts = accounts;
        _accounts.ActiveChanged += OnActive;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public string? AccountKey => _accounts.Active?.Key;

    /// <inheritdoc />
    public bool HasContacts => _accounts.Active is { } active && _accounts.SessionFor(active.Key) is { } session && session.Client.Session.Has(ContactOptions.Capability);

    public void Dispose() => _accounts.ActiveChanged -= OnActive;

    private void OnActive(object? sender, ActiveAccountChangedEventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
