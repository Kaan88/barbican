#if AUTOMATION
// Mirrors: M4 plan section 6.3 (sample data for the hidden editor and task runs). The editors' read seam over the in-memory sample calendars and events; the writes go to SampleCalendarSource (ICalendarWriter), so a saved event shows on the grid and a ticked task on the Tasks view.
// Never part of a shipped build.

using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Editors;

namespace Barbican.App.Views.Calendar.Automation;

/// <summary>The editors' view of a sample run.</summary>
public sealed class SampleEditorSource : IEditorSource
{
    private readonly SampleCalendarSource _grid;

    public SampleEditorSource(SampleCalendarSource grid)
    {
        ArgumentNullException.ThrowIfNull(grid);
        _grid = grid;
        _grid.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;

    public string? AccountKey => _grid.DefaultAccountKey;

    public IReadOnlyList<string> Mine => ["sample@example.org"];

    public IReadOnlyList<JsonNode> Calendars => _grid.CalendarObjects();

    public IReadOnlyList<JsonNode> Events => _grid.EventObjects();

    public IReadOnlySet<string> Subscriptions { get; } = new HashSet<string>(StringComparer.Ordinal);
}
#endif
