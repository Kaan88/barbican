// Mirrors: Bulwark settings-store reads in host-api-calendar.ts:263-308 and assistant/c.calendar.ts:63 (invitation parsing switch, effective time zone).
using Barbican.Core.Calendar;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Calendar.Feeds;
using Barbican.Core.Calendar.Invitations;
using Barbican.Core.Settings.Pages;
using Barbican.Core.Storage;

namespace Barbican.Core.Plugins.Api.Calendar;

/// <summary>The production host: the app's calendar services plus the user's calendar preferences.</summary>
public sealed class PluginCalendarHost : IPluginCalendarHost
{
    private readonly IPrefsStore _prefs;

    public PluginCalendarHost(ICalendarStore store, ICalendarWriter writer, IInvitationService invitations, ICalendarSubscriptions? subscriptions, IPrefsStore prefs, TimeProvider time)
    {
        Store = store ?? throw new ArgumentNullException(nameof(store));
        Writer = writer ?? throw new ArgumentNullException(nameof(writer));
        Invitations = invitations ?? throw new ArgumentNullException(nameof(invitations));
        Subscriptions = subscriptions;
        _prefs = prefs ?? throw new ArgumentNullException(nameof(prefs));
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Undo = new PluginUndoTokens(time);
    }

    public ICalendarStore Store { get; }

    public ICalendarWriter Writer { get; }

    public IInvitationService Invitations { get; }

    public ICalendarSubscriptions? Subscriptions { get; }

    public PluginUndoTokens Undo { get; }

    public TimeProvider Time { get; }

    public TimeZoneInfo LocalZone => SettingDisplayTimeZone.Resolve(_prefs.Current.Str("timeZone", "auto"), Time.LocalTimeZone);

    public string DefaultZoneId => SettingDisplayTimeZone.EventZone(_prefs.Current.Str("timeZone", "auto"), Time.LocalTimeZone);

    public bool InvitationParsingEnabled => CalendarSettings.Read(_prefs.Current).InvitationParsing;
}
