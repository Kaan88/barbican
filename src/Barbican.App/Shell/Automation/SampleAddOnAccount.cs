#if WINDOWS && AUTOMATION
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.App.Shell.Automation;

/// <summary>Exposes the in-process sample mailbox to the Windows add-on integration walkthrough.</summary>
internal sealed class SampleAddOnAccount(SampleActiveMail mail) : IAccountOverview, IAccountSessionSnapshots
{
    private readonly IAccountSession _session = mail.CreateAddOnSession();

    public IReadOnlyList<AccountStatus> Statuses => [new(_session.Account, true, true, true, null, null)];

    public event EventHandler? StatusesChanged { add { } remove { } }

    public Result<AccountSessionSnapshot> CaptureActiveSession() => Result<AccountSessionSnapshot>.Ok(new(_session, 1));

    public Result<AccountSessionSnapshot> CaptureOwnerSession(string localAccountKey) =>
        string.Equals(_session.Account.Key, localAccountKey, StringComparison.Ordinal)
            ? Result<AccountSessionSnapshot>.Ok(new(_session, null))
            : Result<AccountSessionSnapshot>.Fail(new ValidationError("The sample account is unavailable."));

    public bool IsSessionCurrent(AccountSessionSnapshot snapshot) =>
        ReferenceEquals(snapshot.Session, _session) && snapshot.ActiveSelectionGeneration is null or 1;

    public bool TryRunWhileCurrent(IReadOnlyList<AccountSessionSnapshot> snapshots, Func<bool> write) =>
        snapshots.All(IsSessionCurrent) && write();
}
#endif
