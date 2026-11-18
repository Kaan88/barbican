// A calendar store in memory for the writer tests (M4 stream cal-edit).

using System.Text.Json.Nodes;
using Barbican.Core.Calendar;
using Barbican.Core.Common;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Calendar.Editing;

/// <summary>The calendar store as a list in memory: the optimistic patches and restores are recorded, a refresh reads the fake server's data.</summary>
internal sealed class MemoryCalendarStore(FakeCalendars cals) : ICalendarStore
{
    private List<JsonNode> _events = [.. cals.Events()];

    public List<(string Id, JsonObject Patch)> Patches { get; } = [];

    public List<string> Restores { get; } = [];

    public int Refreshes { get; private set; }

    public event EventHandler<CalendarChangedEventArgs>? Changed;

    public Task<Result<CalendarSnapshot>> LoadAsync(string accountKey, CancellationToken ct) => Task.FromResult(Result<CalendarSnapshot>.Ok(Snapshot(accountKey)!));

    public CalendarSnapshot? Snapshot(string accountKey) => new(accountKey, [.. cals.Calendars()], [.. _events], null, null);

    public Task<Result<Unit>> RefreshAsync(string accountKey, CancellationToken ct)
    {
        Refreshes++;
        _events = [.. cals.Events()];
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public JsonNode? Patch(string accountKey, string eventId, JsonObject patch)
    {
        Patches.Add((eventId, (JsonObject)patch.DeepClone()));
        var i = _events.FindIndex(e => string.Equals((string?)e["id"], eventId, StringComparison.Ordinal));
        if (i < 0)
        {
            return null;
        }

        var previous = _events[i];
        var next = (JsonObject)previous.DeepClone();
        foreach (var (path, value) in patch)
        {
            FakeCalendars.ApplyPatch(next, path, value);
        }

        _events[i] = next;
        Changed?.Invoke(this, new CalendarChangedEventArgs(accountKey, false, [eventId]));
        return previous;
    }

    public void Restore(string accountKey, string eventId, JsonNode? previous)
    {
        Restores.Add(eventId);
        var i = _events.FindIndex(e => string.Equals((string?)e["id"], eventId, StringComparison.Ordinal));
        if (i >= 0 && previous is not null)
        {
            _events[i] = previous;
            Changed?.Invoke(this, new CalendarChangedEventArgs(accountKey, false, [eventId]));
        }
    }

    public JsonNode? Event(string id) => _events.Find(e => string.Equals((string?)e["id"], id, StringComparison.Ordinal));
}
