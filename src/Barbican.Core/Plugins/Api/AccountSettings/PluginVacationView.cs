// Mirrors: Bulwark lib/plugin-projection.ts:235-244 vacationToView ({ isEnabled, subject, htmlBody, textBody, fromDate, toDate }; dates are the instants the server holds, or null) and assistant/c.ts:35-50 (vacation.get adds
// forwardActive: whether the out-of-office forwards mail while it is on, true or false, or null when the account has filters but they could not be read). Barbican keeps the reply's period as whole UTC days (the days of
// VacationResponse; Stalwart crates/jmap/src/vacation/{get,set}.rs), so fromDate is the start of its first day and toDate the last second of its last day, both in UTC.
using System.Globalization;
using Barbican.Core.Accounts;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Settings.Vacation;
using Barbican.Core.Sieve;

namespace Barbican.Core.Plugins.Api.AccountSettings;

internal static class PluginVacationView
{
    internal static int Project(PluginValueBuilder b, VacationSettings s, bool? forwardActive, bool withForward)
    {
        var fields = new List<(string, int)>
        {
            ("isEnabled", b.Boolean(s.Enabled)), ("subject", b.String(s.Subject)), ("htmlBody", b.String(s.HtmlBody ?? "")), ("textBody", b.String(s.TextBody)),
            ("fromDate", Day(b, s.From, false)), ("toDate", Day(b, s.To, true)),
        };
        if (withForward) fields.Add(("forwardActive", forwardActive is { } active ? b.Boolean(active) : b.Null()));
        return b.Object([.. fields]);
    }

    /// <summary>Whether the reply forwards mail while it is on; null when the account has filters but they could not be read (the forward lives in the filters script).</summary>
    internal static bool? ForwardActive(IAccountSession session, VacationSettings s) =>
        session.Client.Session.Has(SieveScriptStore.Capability) && s.SieveState is null ? null : s.Forward?.Enabled == true;

    private static int Day(PluginValueBuilder b, DateOnly? day, bool end) =>
        day is { } d ? b.String(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (end ? "T23:59:59Z" : "T00:00:00Z")) : b.Null();
}
