// Test double for IPrefsStore: settings from a JSON object, changeable. Owner mailcore.

using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Tests.Support;

internal sealed class TestPrefs : IPrefsStore
{
    public TestPrefs(string json = "{}") => Set(json);

    public PrefsSnapshot Current { get; private set; } = PrefsSnapshot.FromJson([]);

    public event EventHandler? Changed;

    public void Set(string json)
    {
        Current = PrefsSnapshot.FromJson(JsonNode.Parse(json)!.AsObject());
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
