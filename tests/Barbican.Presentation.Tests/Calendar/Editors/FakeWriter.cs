using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
namespace Barbican.Presentation.Tests.Calendar.Editors;

internal sealed class FakeWriter : ICalendarWriter
{
    public List<EventWrite> Saves { get; } = [];

    public List<(string Id, EditScope Scope, string? Rid, bool Notify)> Deletes { get; } = [];

    public List<(string Calendar, JsonObject Data)> Creates { get; } = [];

    public List<(string Id, JsonObject Patch)> Updates { get; } = [];

    public List<string> Destroys { get; } = [];

    public JmapError? Fail { get; set; }

    private Result<T> Out<T>(T value) => Fail is null ? Result<T>.Ok(value) : Result<T>.Fail(Fail);

    public Task<Result<string>> SaveEventAsync(EventWrite write, CancellationToken ct)
    {
        Saves.Add(write);
        return Task.FromResult(Out("new-id"));
    }

    public Task<Result<Unit>> DeleteEventAsync(string accountKey, string eventId, EditScope scope, string? recurrenceId, bool notify, CancellationToken ct)
    {
        Deletes.Add((eventId, scope, recurrenceId, notify));
        return Task.FromResult(Out(default(Unit)));
    }

    public Task<Result<Unit>> MoveAsync(string accountKey, string occurrenceId, DateTime newStart, TimeSpan duration, CancellationToken ct) => Task.FromResult(Out(default(Unit)));

    public Task<Result<string>> CreateAsync(string accountKey, string calendarId, JsonObject data, CancellationToken ct)
    {
        Creates.Add((calendarId, data));
        return Task.FromResult(Out("created"));
    }

    public Task<Result<Unit>> UpdateAsync(string accountKey, string id, JsonObject patch, CancellationToken ct)
    {
        Updates.Add((id, patch));
        return Task.FromResult(Out(default(Unit)));
    }

    public Task<Result<Unit>> DestroyAsync(string accountKey, string id, CancellationToken ct)
    {
        Destroys.Add(id);
        return Task.FromResult(Out(default(Unit)));
    }
}
