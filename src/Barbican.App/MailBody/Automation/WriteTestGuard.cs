#if AUTOMATION_WRITES || BARBICAN_FILTER_TESTS
// Mirrors: M2 plan §6.3 step 1 (R2-14, decision M2-Q2: a proposal pending the user's OK); Rust automation.rs TEST_ACCOUNT check (the C# port
// names no account in the source: the two .local lists decide). Every rx-* command asks this guard first; it reads both lists again on
// every call (ReadOnlyPolicy.TestWriteRefusal), so editing a list takes effect on the next command. Owner reader.
// Placement: the plan puts these commands in src/Barbican.Automation/Writes, but that project references no Core (it cannot reach the
// account session or JMAP); they live here in the reader's App folder instead (Fable "Requests to other streams").

using Barbican.Core.Accounts;
using Barbican.Core.Storage;

namespace Barbican.App.MailBody.Automation;

/// <summary>The account check of the write-test commands.</summary>
public sealed class WriteTestGuard(IAccountManager accounts, IProfilePaths profile)
{
    /// <summary>The active session when writing to it is allowed, else the refusal line.</summary>
    public (IAccountSession? Session, string? Refusal) Check()
    {
        var active = accounts.Active;
        var refusal = ReadOnlyPolicy.TestWriteRefusal(active?.User, profile.Profile, Environment.GetEnvironmentVariable, ReadOnlyPolicy.ReadAllowList);
        if (refusal is not null)
        {
            return (null, "error " + refusal);
        }

        var session = accounts.SessionFor(active!.Key);
        return session is null ? (null, "error refused: no session for the signed-in account") : (session, null);
    }
}
#endif
