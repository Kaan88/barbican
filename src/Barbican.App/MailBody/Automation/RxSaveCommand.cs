#if AUTOMATION
// Mirrors: M2 plan §6.3 hidden-automation scenario (queued save answers for .eml and zip into the run's temp folder, checked, then deleted by the
// script); spec/automation-protocol.md (M2 reader rows). Only reads from the account (Email/get, download); writes only the queued local file. Owner reader.

using Barbican.Automation;
using Barbican.Core.Accounts;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Presentation.Mail;

namespace Barbican.App.MailBody.Automation;

/// <summary>The <c>rx-save</c> command: <c>queue PATH</c>, <c>eml</c>, <c>zip</c> for the message on screen.</summary>
public sealed class RxSaveCommand(SaveTargets targets, AttachmentSaver saver, ReaderViewModel reader, IActiveMail active, IAccountManager accounts) : IAutomationCommand
{
    public string Name => "rx-save";

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var a = (args ?? string.Empty).Trim();
        if (a.StartsWith("queue ", StringComparison.Ordinal))
        {
            targets.Queue(a[6..].Trim());
            return "queued";
        }

        if (reader.Current.Body is not { } body || active.AccountKey is not { } key || accounts.SessionFor(MailTarget.OfOpen(key, body).Login) is null)
        {
            return "error no message";
        }

        var target = MailTarget.OfOpen(key, body);
        Barbican.Core.Common.Result<Barbican.Core.Common.Unit>? r = a switch
        {
            "eml" => await saver.SaveEmlAsync(target, cancellationToken).ConfigureAwait(true),
            "zip" => await saver.SaveAllAsZipAsync(target, cancellationToken).ConfigureAwait(true),
            _ => null,
        };
        return r is not { } done ? "error rx-save queue PATH|eml|zip" : done.IsOk ? $"saved {targets.LastPath}" : $"error {done.Error!.Message}";
    }
}
#endif
