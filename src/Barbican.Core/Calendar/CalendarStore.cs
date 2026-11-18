// Mirrors: Rust calendar.rs:273 load (Calendar/get with the 11 properties, CalendarEvent/query + get_all in maxObjectsInGet chunks per account), :266 EVENT_PROPS (34 properties), :254 calendar_accounts, :1308 patch_local;
// PORT-PLAN section 3.4 (calendar push: Calendar, CalendarEvent, CalendarEventNotification in the changes engine), RFC 8620 5.2 /changes and 5.5 paged queries, JMAP Calendars (CalendarEvent/get, changes); Bulwark stores/calendar-store.ts
// (fetchCalendars, fetchEvents; onBeforeFetchEvents :1179, onFetchingEvents), lib/plugin-hook-catalog.ts:171-172, :205; Stalwart crates/jmap/src/calendar_event/{query,get,changes}.rs (state strings are per type, ids paged by position,
// maxObjectsInGet is enforced). G-044 (calendars live). Owner cal-data.

using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Jmap;
using Barbican.Core.Plugins.Hooks;
using Barbican.Core.Storage;
using Barbican.Core.Sync;

namespace Barbican.Core.Calendar;

/// <summary>The default <see cref="ICalendarStore"/>.</summary>
public sealed partial class CalendarStore : ICalendarStore, IDisposable
{
    /// <summary>The 11 Calendar properties (Rust calendar.rs:285).</summary>
    public static readonly IReadOnlyList<string> CalendarProps =
        ["id", "name", "color", "isDefault", "isVisible", "sortOrder", "myRights", "defaultAlertsWithTime", "defaultAlertsWithoutTime", "isSubscribed", "includeInAvailability"];

    /// <summary>The 34 CalendarEvent properties (Rust calendar.rs:266 EVENT_PROPS).</summary>
    public static readonly IReadOnlyList<string> EventProps =
    [
        "id", "@type", "uid", "title", "description", "start", "duration", "timeZone", "showWithoutTime", "calendarIds", "locations", "virtualLocations", "recurrenceRules", "excludedRecurrenceRules", "recurrenceOverrides",
        "recurrenceId", "status", "freeBusyStatus", "privacy", "participants", "organizerCalendarAddress", "replyTo", "isOrigin", "alerts", "useDefaultAlerts", "due", "progress", "percentComplete", "priority", "updated",
        "excludedRecurrenceIds", "keywords", "color", "sequence",
    ];

    private readonly IAccountManager _accounts;
    private readonly IHookBus _hooks;
    private readonly IPrefsStore? _prefs;
    private readonly ICalendarPush? _push;
    private readonly Lock _lock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Held> _held = new(StringComparer.Ordinal);
    private readonly List<Task> _refreshes = [];

    public CalendarStore(IAccountManager accounts, IHookBus hooks, IPrefsStore? prefs = null, ICalendarPush? push = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(hooks);
        _accounts = accounts;
        _hooks = hooks;
        _prefs = prefs;
        _push = push;
        if (push is not null)
        {
            push.Changed += OnPush;
        }
    }

    /// <inheritdoc/>
    public event EventHandler<CalendarChangedEventArgs>? Changed;

    /// <inheritdoc/>
    public CalendarSnapshot? Snapshot(string accountKey)
    {
        lock (_lock)
        {
            return _held.TryGetValue(accountKey, out var h) ? h.Snapshot : null;
        }
    }

    /// <inheritdoc/>
    public JsonNode? Patch(string accountKey, string eventId, JsonObject patch)
    {
        ArgumentNullException.ThrowIfNull(patch);
        JsonNode? previous = null;
        var id = CalendarIds.BaseId(eventId);
        lock (_lock)
        {
            if (_held.TryGetValue(accountKey, out var h) && h.Events.FindIndex(e => string.Equals(e.Str("id"), id, StringComparison.Ordinal)) is var i and >= 0)
            {
                previous = h.Events[i];

                // a copy, so a snapshot a view already holds does not change under it
                var changed = (JsonObject)previous.DeepClone();
                JsonPointerPatch.Apply(changed, patch);
                var events = new List<JsonNode>(h.Events) { [i] = changed };
                h.SetEvents(accountKey, events);
            }
        }

        if (previous is not null)
        {
            Raise(new CalendarChangedEventArgs(accountKey, false, [id]));
        }

        return previous;
    }

    /// <inheritdoc/>
    public void Restore(string accountKey, string eventId, JsonNode? previous)
    {
        if (previous is null)
        {
            return;
        }

        var id = CalendarIds.BaseId(eventId);
        var found = false;
        lock (_lock)
        {
            if (_held.TryGetValue(accountKey, out var h) && h.Events.FindIndex(e => string.Equals(e.Str("id"), id, StringComparison.Ordinal)) is var i and >= 0)
            {
                found = true;
                var events = new List<JsonNode>(h.Events) { [i] = previous.DeepClone() };
                h.SetEvents(accountKey, events);
            }
        }

        if (found)
        {
            Raise(new CalendarChangedEventArgs(accountKey, false, [id]));
        }
    }

    /// <summary>Waits for the refreshes a push started (tests, shutdown).</summary>
    public async Task DrainAsync()
    {
        Task[] running;
        lock (_lock)
        {
            _ = _refreshes.RemoveAll(t => t.IsCompleted);
            running = [.. _refreshes];
        }

        await Task.WhenAll(running).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_push is not null)
        {
            _push.Changed -= OnPush;
        }
    }

    private void Raise(CalendarChangedEventArgs e) => Changed?.Invoke(this, e);

    private string? KeyOf(IJmapClient client) => _accounts.Accounts.Select(a => a.Key).FirstOrDefault(k => ReferenceEquals(_accounts.SessionFor(k)?.Client, client));

    // a push for a calendar type: apply /changes to a login whose calendar is loaded (others load when the Calendar app opens)
    private void OnPush(object? sender, CalendarPushEventArgs e)
    {
        if (!e.Types.Contains("Calendar", StringComparer.Ordinal) && !e.Types.Contains("CalendarEvent", StringComparer.Ordinal))
        {
            return;
        }

        if (KeyOf(e.Client) is not { } key || Snapshot(key) is null)
        {
            return;
        }

        var task = RefreshQuietlyAsync(key);
        lock (_lock)
        {
            _refreshes.Add(task);
        }
    }

#pragma warning disable CA1031 // a push-driven refresh has no caller to tell: the next push or the next open of the calendar loads again
    private async Task RefreshQuietlyAsync(string key)
    {
        try
        {
            _ = await RefreshAsync(key, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // see above
        }
    }
#pragma warning restore CA1031

    /// <summary>What one login's calendars and events are, and how to get at them again.</summary>
    private sealed class Held(string primary)
    {
        public string Primary { get; } = primary;

        public List<JsonNode> Calendars { get; set; } = [];

        public List<JsonNode> Events { get; private set; } = [];

        /// <summary>The state strings per server account: (Calendar, CalendarEvent).</summary>
        public Dictionary<string, (string? Calendar, string? Event)> States { get; } = new(StringComparer.Ordinal);

        public CalendarSnapshot Snapshot { get; private set; } = new(string.Empty, [], [], null, null);

        public void SetEvents(string key, List<JsonNode> events)
        {
            Events = events;
            Rebuild(key);
        }

        public void Rebuild(string key)
        {
            var (cal, ev) = States.TryGetValue(Primary, out var s) ? s : (null, null);
            Snapshot = new CalendarSnapshot(key, [.. Calendars], [.. Events], cal, ev);
        }
    }

    private sealed record AccountLoad(List<JsonNode> Calendars, List<JsonNode> Events, string? CalendarState, string? EventState);
}
