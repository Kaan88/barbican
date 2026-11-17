#if AUTOMATION
// Mirrors: M2 plan 4.7 (`import-sim PATH MAILBOX`, FakeJmap only), 6.5 hidden scenario and the import_progress feel scenario;
// spec/automation-protocol.md (M2 unified rows). Owner unified.
// Imports PATH (an .mbox or .eml file) into MAILBOX (an id, a role such as "inbox", or a name) of the account on screen through the same
// MailTransfer the folder menu uses (the queued pick answers PATH). Refused unless the account's server is on the loopback interface (the
// FakeJmap server of a hidden run): a real account is never written to by this command. "cancel" stops a running import.

using Barbican.Automation;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Presentation.Unified;

namespace Barbican.App.Views.Unified.Automation;

/// <summary>The <c>import-sim</c> command: <c>import-sim PATH MAILBOX</c>, <c>import-sim cancel</c>, <c>import-sim state</c>.</summary>
public sealed class ImportSimCommand(IUnifiedLogins logins, MailTransfer transfer, TransferPicks picks) : IAutomationCommand
{
    public string Name => "import-sim";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var a = (args ?? string.Empty).Trim();
        if (string.Equals(a, "cancel", StringComparison.Ordinal))
        {
            transfer.Cancel();
            return "ok cancel";
        }

        if (string.Equals(a, "state", StringComparison.Ordinal))
        {
            return $"import running={transfer.IsRunning} done={transfer.Done} summary={transfer.Summary}";
        }

        var split = a.LastIndexOf(' ');
        if (split <= 0)
        {
            return "error import-sim PATH MAILBOX|cancel|state";
        }

        var (path, mailbox) = (a[..split].Trim().Trim('"'), a[(split + 1)..]);
        if (logins.Active is not { } active || logins.ClientOf(active) is not { } client)
        {
            return "error no account on screen";
        }

        if (!logins.IsTestServer(active))
        {
            return "error import-sim writes only to a test server (FakeJmap: loopback or the sample run's)";
        }

        var target = await ResolveAsync(client, active, mailbox, cancellationToken).ConfigureAwait(true);
        if (target is null)
        {
            return "error no folder " + mailbox;
        }

        picks.Queue(path);
        var r = await transfer.ImportAsync(target, cancellationToken).ConfigureAwait(true);
        return r is not { } done ? "stopped " + transfer.Summary
            : done.IsOk ? $"imported {done.Value!.Imported} already {done.Value.Already} errors {done.Value.Errors.Count}"
            : "error " + done.Error!.Message;
    }

    private static async Task<MailboxRef?> ResolveAsync(IJmapClient client, string key, string wanted, CancellationToken ct)
    {
        var account = client.Session.MailAccountId;
        var list = await UnifiedFetcher.MailboxesAsync(new UnifiedSource(key, client, account, false), ct).ConfigureAwait(true);
        if (!list.IsOk)
        {
            return null;
        }

        var hit = list.Value!.FirstOrDefault(m => string.Equals((string?)m["id"], wanted, StringComparison.Ordinal))
            ?? list.Value!.FirstOrDefault(m => string.Equals((string?)m["role"], wanted, StringComparison.OrdinalIgnoreCase))
            ?? list.Value!.FirstOrDefault(m => string.Equals((string?)m["name"], wanted, StringComparison.OrdinalIgnoreCase));
        return hit is null ? null : new MailboxRef(key, account, (string)hit["id"]!, (string?)hit["role"]);
    }
}
#endif
