// A recording ICalendarEditors for the tasks tests. M4 stream cal-ui.

using Barbican.Core.Calendar.Editing;
using Barbican.Presentation.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Tasks;

internal sealed class RecordingEditors : ICalendarEditors
{
    public List<string> Opened { get; } = [];

    public Task OpenQuickCreateAsync(NewEventRequest request, CancellationToken ct) => Task.CompletedTask;

    public Task OpenNewEventAsync(NewEventRequest request, CancellationToken ct) => Task.CompletedTask;

    public Task OpenAsync(string occurrenceId, CancellationToken ct)
    {
        Opened.Add(occurrenceId);
        return Task.CompletedTask;
    }

    public Task OpenNewTaskAsync(string? calendarId, DateOnly? due, CancellationToken ct) => Task.CompletedTask;

    public Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct) => Task.FromResult<(EditScope, bool)?>(null);
}
