// Fakes of the calendar streams' services (cal-data store, cal-edit writer and rules) so the editor, task and menu view models are tested before those streams land. M4 stream cal-ui.

using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Editors;
namespace Barbican.Presentation.Tests.Calendar.Editors;

internal sealed class FakeSource : ICalendarSource
{
    public string? AccountKey { get; set; } = "acct";

    public IReadOnlyList<string> Mine { get; set; } = ["me@x.example"];

    public IReadOnlyList<JsonNode> Calendars { get; set; } =
    [
        JsonNode.Parse("""{"id":"work","name":"Work","color":"#3b82f6","isDefault":true}""")!,
        JsonNode.Parse("""{"id":"home","name":"Home","color":"#22c55e"}""")!,
        JsonNode.Parse("""{"id":"feed","name":"Holidays","color":"#ef4444"}""")!,
    ];

    public IReadOnlyList<JsonNode> Events { get; set; } = [];

    public IReadOnlySet<string> Subscriptions { get; set; } = new HashSet<string>(["feed"], StringComparer.Ordinal);

    public event EventHandler? Changed;

    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
