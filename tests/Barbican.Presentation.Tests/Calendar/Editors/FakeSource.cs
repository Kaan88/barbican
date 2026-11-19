// Fakes of the calendar streams' services (cal-data store, cal-edit writer and rules) so the editor, task and menu view models are tested before those streams land. M4 stream cal-ui.

using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Editors;
namespace Barbican.Presentation.Tests.Calendar.Editors;

internal sealed class FakeSource : IEditorSource
{
    private string? _accountKey = "acct";
    public long AccountVersion { get; private set; }
    public string? AccountKey
    {
        get => _accountKey;
        set
        {
            if (!string.Equals(_accountKey, value, StringComparison.Ordinal))
            {
                _accountKey = value;
                AccountVersion++;
            }
        }
    }

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
