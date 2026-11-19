// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:25-30; host-added selection/session ABA guard.
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
namespace Barbican.Core.Accounts;

public sealed partial class AccountManager
{
    public Result<AccountSessionSnapshot> CaptureActiveSession()
    {
        lock (_lock)
        {
            return _activeKey is { } key && _entries.TryGetValue(key, out var entry) && entry.Session is { } session
                ? Result<AccountSessionSnapshot>.Ok(new(session, _selectionGeneration)) : NoSession();
        }
    }
    public Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAccountKey);
        lock (_lock)
        {
            return _entries.TryGetValue(localAccountKey, out var entry) && entry.Session is { } session
                ? Result<AccountSessionSnapshot>.Ok(new(session, null)) : NoSession();
        }
    }
    public bool IsSessionCurrent(AccountSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_lock)
        {
            var key = snapshot.Session.Account.Key;
            return _entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Session, snapshot.Session)
                && (snapshot.ActiveSelectionGeneration is null || (snapshot.ActiveSelectionGeneration == _selectionGeneration
                    && string.Equals(_activeKey, key, StringComparison.Ordinal)));
        }
    }
    private static Result<AccountSessionSnapshot> NoSession() => Result<AccountSessionSnapshot>.Fail(new AuthError("No active account session.", NeedsSignIn: false));
}
