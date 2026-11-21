// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.calendar.ts:165-181 doCalendarUndoDelete (c.ts:26,87: calendar:write): puts back an event calendar.deleteEvent deleted, from the host's copy, with the same uid,
// sending no invitations; when it cannot be created (another add-on stopped it, the server refused) the token stays for another try; c.undo.ts:46-65 (only the plugin that got the token, in the same login,
// for the same kind, within 30 minutes; one undo at a time per token). The calendar the event was in must still exist and allow writing: the user's rights are checked again now, not trusted from the delete.
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Calendar;

internal sealed class PluginCalendarUndoDelete(IPluginCalendarHost host) : PluginCalendarMethod("calendar.undoDelete", "calendar:write", host, true)
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => PluginMailArgs.IsId(PluginMailArgs.At(arguments, 0)) ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginCalendarCall call, PluginValue arguments, CancellationToken ct)
    {
        var claim = Host.Undo.Begin(arguments.Item(0).StringValue, call.PluginId, call.AccountKey, PluginCalendarDeleteEvent.Kind);
        if (claim is null) return Fail(PluginErrorCode.Unavailable);
        try
        {
            var snapshot = await PluginCalendarData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
            if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
            var data = (System.Text.Json.Nodes.JsonObject)claim.Data.DeepClone();
            var feeds = await PluginCalendarData.FeedCalendarsAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
            if (PluginCalendarData.CalendarById(snapshot, PluginCalendarProjection.FirstTrueCalendar(data)) is not { } calendar || !PluginCalendarData.CanCreateIn(calendar, feeds)) return Fail(PluginErrorCode.PermissionDenied);
            if (!await call.BeginMutationAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
            var created = await Host.Writer.SaveEventAsync(new EventWrite(call.AccountKey, null, data, EditScope.WholeEvent, null, false), ct).ConfigureAwait(false);
            if (!created.IsOk) return Fail(PluginErrorCode.Failed);
            claim.Spend();
            claim = null;
            var b = new PluginValueBuilder();
            return Success(b, b.Object(("id", b.String(created.Value!))));
        }
        finally
        {
            claim?.Release();
        }
    }
}
