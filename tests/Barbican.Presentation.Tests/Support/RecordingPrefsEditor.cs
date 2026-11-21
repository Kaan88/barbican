// A preferences editor that remembers what was put, for tests of commands that write settings.

using System.Text.Json.Nodes;
using Barbican.Core.Storage;

namespace Barbican.Presentation.Tests.Support;

internal sealed class RecordingPrefsEditor : IPrefsEditor
{
    public Dictionary<string, JsonNode?> Values { get; } = new(StringComparer.Ordinal);

    public void Put(string key, JsonNode? value) => Values[key] = value;

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
}
