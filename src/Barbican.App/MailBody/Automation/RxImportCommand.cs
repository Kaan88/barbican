#if AUTOMATION_WRITES
// Mirrors: Rust automation.rs rx-import (automation-writes feature); M2 plan §6.3 step 1. `rx-import TOKEN`: uploads and imports one synthetic
// message marked with TOKEN into the Inbox of the signed-in account; replies `ok EMAILID`. Refused unless WriteTestGuard allows it. Owner reader.

using Barbican.Automation;
using Barbican.Core.Jmap.Transport;
using Barbican.Core.Mail.Reader;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>rx-import</c> command (AUTOMATION_WRITES builds only).</summary>
public sealed class RxImportCommand(WriteTestGuard guard, TimeProvider time) : IAutomationCommand
{
    public string Name => "rx-import";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var token = (args ?? string.Empty).Trim();
        if (!WriteTestMessages.ValidToken(token))
        {
            return "error rx-import TOKEN (8-40 letters and digits)";
        }

        var (session, refusal) = guard.Check();
        if (session is null)
        {
            return refusal!;
        }

        if (session.Client is not JmapClient client || client.Session.MailAccountId is not { } account)
        {
            return "error the session has no mail account";
        }

        var r = await WriteTestMessages.ImportAsync(client, account, token, time.GetUtcNow(), cancellationToken).ConfigureAwait(true);
        return r.IsOk ? "ok " + r.Value : "error " + r.Error!.Message;
    }
}
#endif
