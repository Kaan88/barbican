#if AUTOMATION_WRITES
// Mirrors: Rust automation.rs rx-destroy (automation-writes feature); M2 plan §6.3 step 1. `rx-destroy TOKEN ID...`: destroys the given
// messages only when every one carries TOKEN (checked on the server's copy); otherwise nothing is destroyed. Replies `ok destroyed N`.
// The token is the first argument (the plan's `rx-destroy ID...` could not tell which run's mark to check). Owner reader.

using System.Globalization;
using Barbican.Automation;
using Barbican.Core.Mail.Reader;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>rx-destroy</c> command (AUTOMATION_WRITES builds only).</summary>
public sealed class RxDestroyCommand(WriteTestGuard guard) : IAutomationCommand
{
    public string Name => "rx-destroy";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var parts = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || !WriteTestMessages.ValidToken(parts[0]))
        {
            return "error rx-destroy TOKEN ID...";
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

        var r = await WriteTestMessages.DestroyAsync(session.Client, account, parts[0], parts[1..], cancellationToken).ConfigureAwait(true);
        return r.IsOk ? string.Create(CultureInfo.InvariantCulture, $"ok destroyed {r.Value}") : "error " + r.Error!.Message;
    }
}
#endif
