// Mirrors: Rust unified.rs:139 sum_other_logins (the unread of the logins not on screen; a negative count is unknown), :400 unread_total;
// Bulwark lib/account-inbox-unread.ts. M2 plan 4.6 (IUnifiedUnread). Owner unified.

using Barbican.Core.Accounts;
using Barbican.Core.Mail.Unified;

namespace Barbican.Presentation.Unified;

/// <summary>The inbox unread of the signed-in accounts that are not on screen, from the account rows.</summary>
public sealed class UnifiedUnread : IUnifiedUnread, IDisposable
{
    private readonly IAccountOverview _overview;

    public UnifiedUnread(IAccountOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);
        _overview = overview;
        _overview.StatusesChanged += OnChanged;
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public long Total
    {
        get
        {
            var statuses = _overview.Statuses;
            var active = statuses.FirstOrDefault(s => s.IsActive)?.Account.Key ?? string.Empty;
            return UnifiedRows.SumOtherLogins(statuses.ToDictionary(s => s.Account.Key, s => (long)(s.InboxUnread ?? -1), StringComparer.Ordinal), active);
        }
    }

    public void Dispose() => _overview.StatusesChanged -= OnChanged;

    private void OnChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}
