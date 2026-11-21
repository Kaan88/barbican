// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:260-270 doCalendarListCalendars and host-api.ts:308,2417 (calendar:read).
// Deviation: Bulwark appends a virtual "Birthdays" calendar when the user shows it and the plugin also holds contacts:read; Barbican has no contact-fed calendar in this
// API, so the list is the login's calendars (its own and the shared accounts' it shows) as the calendar views hold them.
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarListCalendars(IPluginCalendarHost host) : PluginCalendarMethod("calendar.listCalendars", "calendar:read", host, false)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. snapshot.Calendars.Select(c => PluginCalendarProjection.Calendar(b, c))]));
    }
}
