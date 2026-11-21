// Mirrors: Bulwark lib/plugin-sandbox/host-api-calendar.ts:1-8 (plugins read and write through the calendar and task stores, so hooks run and the open calendar shows the change)
// and assistant/c.calendar.ts:1-4. The Barbican equivalents of those stores: the live calendar data (ICalendarStore) and the one writer the UI uses (ICalendarWriter).
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>The services the calendar and task add-on methods run on. Nothing here is raw JMAP: reads come from the store the views show, writes go through the writer the views use.</summary>
public interface IPluginCalendarHost
{
    ICalendarStore Store { get; }

    ICalendarWriter Writer { get; }

    IInvitationService Invitations { get; }

    /// <summary>Feeds are read-only calendars whatever their rights say (Bulwark calendar-editability.ts:14-26); null when subscriptions are not available.</summary>
    ICalendarSubscriptions? Subscriptions { get; }

    PluginUndoTokens Undo { get; }

    TimeProvider Time { get; }

    /// <summary>The user's clock, for ranges written without a zone.</summary>
    TimeZoneInfo LocalZone { get; }

    /// <summary>The IANA zone an event or task due time without one is stored in (Bulwark getEffectiveTimeZone).</summary>
    string DefaultZoneId { get; }

    /// <summary>The "read invitations" calendar setting (Bulwark calendarInvitationParsingEnabled, c.calendar.ts:63).</summary>
    bool InvitationParsingEnabled { get; }
}
