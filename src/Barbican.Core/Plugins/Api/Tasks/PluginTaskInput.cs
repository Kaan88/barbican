// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:573-576 TASK_FIELDS, :975-1030 (dueOf and the field checks of doTasksCreate / doTasksUpdate); lib/plugin-types.ts:614-640 TaskInput.
// Unknown properties are refused; title 1-500, description 10,000, priority 0-9, due a date or a wall-clock time without Z, alerts / links / relatedTo / keywords as PluginTaskMapReader reads them.
using Barbican.Core.Plugins.Api.Calendar;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Tasks;

internal sealed class PluginTaskInput
{
    internal const int MaxTitle = 500;
    internal const int MaxDescription = 10_000;
    private static readonly string[] Fields = ["title", "description", "due", "priority", "calendarId", "alerts", "links", "relatedTo", "keywords"];

    internal string? Title { get; private set; }

    internal string? Description { get; private set; }

    /// <summary>Whether due was named; <see cref="Due"/> null then clears it.</summary>
    internal bool HasDue { get; private set; }

    internal PluginWhen? Due { get; private set; }

    internal int? Priority { get; private set; }

    internal string? CalendarId { get; private set; }

    internal PluginTaskMapChange? Alerts { get; private set; }

    internal PluginTaskMapChange? Links { get; private set; }

    internal PluginTaskMapChange? RelatedTo { get; private set; }

    internal PluginTaskMapChange? Keywords { get; private set; }

    internal bool IsEmpty { get; private set; }

    internal static bool TryParse(PluginValue? value, bool create, out PluginTaskInput input)
    {
        input = new PluginTaskInput();
        if (!PluginMailArgs.TryOptions(value, Fields, true, out var o) || o is null) return false;
        input.IsEmpty = o.PropertyNames.All(n => PluginMailArgs.Prop(o, n) is null);
        if (input.IsEmpty && !create) return false;
        if (create && PluginMailArgs.Prop(o, "title") is null) return false;
        if (PluginMailArgs.Prop(o, "title") is { } title)
        {
            if (!PluginMailArgs.TryString(title, 100_000, out var text) || text.Trim() is not { Length: > 0 and <= MaxTitle } trimmed) return false;
            input.Title = trimmed;
        }

        if (PluginMailArgs.Prop(o, "description") is { } description)
        {
            if (!PluginMailArgs.TryString(description, 100_000, out var text) || text.Trim() is not { Length: <= MaxDescription } trimmed) return false;
            input.Description = trimmed;
        }

        if (o.PropertyNames.Contains("due", StringComparer.Ordinal) && o.Property("due").Kind != PluginValueKind.Undefined)
        {
            input.HasDue = true;
            var due = o.Property("due");
            if (due.Kind != PluginValueKind.Null)
            {
                if (!PluginMailArgs.TryString(due, 64, out var text) || !PluginWhenParser.TryRead(text, out var when) || when.Utc) return false;
                input.Due = when;
            }
        }

        if (!PluginMailArgs.TryInteger(PluginMailArgs.Prop(o, "priority"), 0, 9, -1, out var priority)) return false;
        input.Priority = priority < 0 ? null : priority;
        if (PluginMailArgs.Prop(o, "calendarId") is { } id)
        {
            if (!PluginMailArgs.IsId(id)) return false;
            input.CalendarId = id.StringValue;
        }

        if (!PluginTaskMapReader.TryAlerts(PluginMailArgs.Prop(o, "alerts"), out var alerts) || !PluginTaskMapReader.TryLinks(PluginMailArgs.Prop(o, "links"), out var links)
            || !PluginTaskMapReader.TryRelations(PluginMailArgs.Prop(o, "relatedTo"), out var related) || !PluginTaskMapReader.TryKeywords(PluginMailArgs.Prop(o, "keywords"), out var keywords))
        {
            return false;
        }

        (input.Alerts, input.Links, input.RelatedTo, input.Keywords) = (alerts, links, related, keywords);
        return true;
    }
}
