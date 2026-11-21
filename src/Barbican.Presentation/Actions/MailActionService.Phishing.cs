// Mirrors: Bulwark lib/phishing-report-flow.ts reportEmailsAsPhishing :84 (after the report went out the message moves to Junk "like Mark as spam (plugins'
// onBeforeEmailSpamToggle still runs), with $phishing, and read: it shouldn't count as new mail in Junk") and undoPhishingReports :158 (back to where it was,
// $junk and $phishing cleared); lib/jmap/client.ts markAsSpam :4577, undoSpam :4616. Owner actions.

using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Safety;
using Barbican.Core.Mail.Write.Payloads;
using Barbican.Core.Plugins.Hooks;

namespace Barbican.Presentation.Actions;

public sealed partial class MailActionService
{
    /// <summary>Moves messages whose report went out to Junk with <c>$junk</c> and <c>$phishing</c>, read; Undo moves them back and clears both flags. <paramref name="toast"/> is the one line shown.</summary>
    public Task<Result<Unit>> MoveReportedToJunkAsync(IReadOnlyList<MailTarget> targets, Func<int, string> toast, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(toast);
        var dest = RoleFolder(MailboxRole.Junk);
        if (dest is null)
        {
            return Task.FromResult(Result<Unit>.Fail(Failed(new ValidationError("There is no Junk folder."))));
        }

        var flags = new Dictionary<string, bool>(StringComparer.Ordinal) { ["$junk"] = true, ["$notjunk"] = false, [PhishingLinks.Keyword] = true, ["$seen"] = true };
        var back = new Dictionary<string, bool>(StringComparer.Ordinal) { ["$junk"] = false, ["$notjunk"] = true, [PhishingLinks.Keyword] = false };
        return RemoveFlowAsync(targets, (_, _) => new RemovalOp(
            "Reported as phishing",
            toast,
            true,
            (w, g, c) => w.MoveWithKeywordsAsync(Ids(g), dest.Id, flags, c),
            (ids, c) => AllowAsync(HookNames.OnBeforeEmailSpamToggle, () => new EmailSpamPayload(ids, true), c),
            ids => hooks.Fire(HookNames.OnEmailSpamToggle, () => new EmailSpamPayload(ids, true))) { KeywordsBack = back, Verb = "reported as phishing" }, ct);
    }
}
