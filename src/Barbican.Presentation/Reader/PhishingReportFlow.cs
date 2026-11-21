// Mirrors: Bulwark components/email/phishing-report-host.tsx (the "Report phishing?" dialog with the address the report goes to, one report per message, the
// outcome toast with Undo), lib/phishing-report-flow.ts reportEmailsAsPhishing :84 (send one report per message, then move it to Junk with $phishing), hooks/
// use-phishing-report.ts requestPhishingReport :36 (only the messages that can be reported are asked about, one confirmation names every address),
// locales/en/common.json phishing_report.*. Never a report without the user's yes; the Undo moves the messages back (the report stays sent). Owner reader.

using Barbican.Core.Common;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Safety;
using Barbican.Core.OrganizationPolicy;
using Barbican.Presentation.Actions;
using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Reader;

/// <summary>Asks, reports and moves to Junk the messages the user marks as phishing.</summary>
public sealed class PhishingReportFlow(IPhishingReportSetups setups, IPhishingReporter reporter, MailActionService actions, IDialogService dialogs, IMailboxDirectory folders, IToastService toasts)
{
    /// <summary>The outcome line of the last run (automation).</summary>
    public string? LastResult { get; private set; }

    /// <summary>Whether any of <paramref name="targets"/> can be reported from a folder of <paramref name="mailboxRole"/> now.</summary>
    public bool CanReport(IReadOnlyList<MailTarget> targets, string? mailboxRole) => Reportable(targets, mailboxRole).Count > 0;

    /// <summary>Asks "Report phishing?" for the messages that can be reported, then reports them one by one and moves each to Junk.</summary>
    public async Task<Result<Unit>> RunAsync(IReadOnlyList<MailTarget> targets, string? mailboxRole, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(targets);
        var list = Reportable(targets, mailboxRole);
        if (list.Count == 0)
        {
            return Done("Nothing to report.");
        }

        // the policy as it is now: an address an admin took away since the menu opened ends the flow before the question
        var current = new Dictionary<string, PhishingReportPolicy>(StringComparer.Ordinal);
        foreach (var account in list.Select(t => t.Login).Distinct(StringComparer.Ordinal))
        {
            current[account] = await setups.ReadAsync(account, ct).ConfigureAwait(true);
        }

        list = [.. list.Where(t => current[t.Login].IsOn)];
        if (list.Count == 0)
        {
            return Done("Reporting is not set up for this account.");
        }

        // a selection across accounts may go to more than one address: name each
        var addresses = AddressesOf(list, current);
        var question = list.Count == 1
            ? $"The message goes to {addresses} and moves to Junk."
            : $"Each of the {list.Count} messages goes to {addresses} as its own report and moves to Junk.";
        if (await dialogs.ShowAsync(new ConfirmDialogRequest("Report phishing?", question, "Report", Danger: true), ct).ConfigureAwait(true) != true)
        {
            return Done("Cancelled.");
        }

        var (reported, failed) = await SendAllAsync(list, current, ct).ConfigureAwait(true);
        if (failed.Count > 0)
        {
            var text = failed.Count == 1 ? "The message couldn't be reported." : $"{failed.Count} messages couldn't be reported.";
            toasts.Show(new ToastRequest($"{text} {failed[0]}", IsError: true));
        }

        if (reported.Count == 0)
        {
            return Done(failed.Count > 0 ? "Not reported." : "Nothing to report.");
        }

        var to = AddressesOf(reported, current);
        var moved = await actions.MoveReportedToJunkAsync(reported, n => n == 1 ? $"Reported to {to} and moved to Junk." : $"{n} messages reported to {to} and moved to Junk.", ct).ConfigureAwait(true);
        if (!moved.IsOk)
        {
            // the move failed (its own toast says why): the report went out all the same
            toasts.Show(new ToastRequest(reported.Count == 1 ? $"Reported to {to}, but the message couldn't be moved to Junk." : $"Reported to {to}, but {reported.Count} messages couldn't be moved to Junk."));
        }

        return Done(failed.Count > 0 ? $"Reported {reported.Count}, failed {failed.Count}." : $"Reported {reported.Count} to {to}.");
    }

    private static string AddressesOf(IEnumerable<MailTarget> targets, Dictionary<string, PhishingReportPolicy> current) =>
        string.Join(", ", targets.Select(t => current[t.Login].Address).Distinct(StringComparer.Ordinal));

    // one report per message (report desks discard reports with several messages attached, RFC 5965 section 2(e))
    private async Task<(List<MailTarget> Reported, List<string> Failed)> SendAllAsync(List<MailTarget> list, Dictionary<string, PhishingReportPolicy> current, CancellationToken ct)
    {
        var reported = new List<MailTarget>();
        var failed = new List<string>();
        foreach (var target in list)
        {
            var sent = await reporter.ReportAsync(target, current[target.Login], ct).ConfigureAwait(true);
            if (sent.IsOk)
            {
                reported.Add(target);
            }
            else
            {
                failed.Add(sent.Error!.Message);
            }
        }

        return (reported, failed);
    }

    private List<MailTarget> Reportable(IReadOnlyList<MailTarget> targets, string? mailboxRole)
    {
        var roles = folders.Tree.All.Where(f => f.Role != MailboxRole.None).ToDictionary(f => f.Id, f => f.Role.ToString().ToLowerInvariant(), StringComparer.Ordinal);
        return
        [
            .. targets.Where(t => setups.Cached(t.Login).IsOn &&
                PhishingReportRules.IsReportable(t.Keywords, mailboxRole, t.MailboxIds.Where(m => m.Value).Select(m => roles.GetValueOrDefault(m.Key)))),
        ];
    }

    private Result<Unit> Done(string text)
    {
        LastResult = text;
        return Result<Unit>.Ok(default);
    }
}
