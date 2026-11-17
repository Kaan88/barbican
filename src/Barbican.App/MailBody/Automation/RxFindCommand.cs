#if AUTOMATION_WRITES
// Mirrors: Rust automation.rs rx-find (automation-writes feature); M2 plan §6.3 step 1. `rx-find TOKEN`: the ids of the messages that carry
// TOKEN in their subject and their X-Barbican-Write-Test header; replies `ok ID...` or `ok -`. Read-only, but guarded like the writes so a
// script cannot use it to probe an account it may not touch. Owner reader.

using Barbican.Automation;
using Barbican.Core.Mail.Reader;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>rx-find</c> command (AUTOMATION_WRITES builds only).</summary>
public sealed class RxFindCommand(WriteTestGuard guard) : IAutomationCommand
{
    public string Name => "rx-find";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var token = (args ?? string.Empty).Trim();
        if (!WriteTestMessages.ValidToken(token))
        {
            return "error rx-find TOKEN (8-40 letters and digits)";
        }

        var (session, refusal) = guard.Check();
        if (session is null)
        {
            return refusal!;
        }

        if (session.Client.Session.MailAccountId is not { } account)
        {
            return "error the session has no mail account";
        }

        var r = await WriteTestMessages.FindAsync(session.Client, account, token, cancellationToken).ConfigureAwait(true);
        return !r.IsOk ? "error " + r.Error!.Message : r.Value!.Count == 0 ? "ok -" : "ok " + string.Join(' ', r.Value!);
    }
}
#endif
