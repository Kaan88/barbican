// Scripted collaborators of the assistant tests (a client that answers from a list, tools that count their runs, in-memory host settings and secrets). No live provider and no real host is involved.

using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Plugin.Assistant.Tests;

internal sealed class AssistantTestFakes
{
    internal sealed class ScriptedClient(Func<int, AssistantMessage> script) : IAssistantClient
    {
        public int Calls { get; private set; }

        public Task<AssistantResult<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct) =>
            Task.FromResult(AssistantResult<AssistantMessage>.Ok(script(++Calls)));
    }

    internal sealed class CountingTools(bool writes, string name = "do_thing") : IAssistantTools
    {
        public int Runs { get; private set; }

        public IReadOnlyList<AssistantToolSpec> Specs(string accountKey, AssistantActionMode mode) => [new(name, "does a thing", [], writes)];

        public Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult("done");
        }
    }

    /// <summary>The host's per-account settings, in memory.</summary>
    internal sealed class MemorySettings : IPluginSettings
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<(string, string), string> _values = [];

        public event EventHandler? Changed;

        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

        public string? Read(string accountKey, string key)
        {
            lock (_lock)
            {
                return _values.TryGetValue((accountKey, key), out var value) ? value : null;
            }
        }

        public Task SetAsync(string accountKey, IReadOnlyDictionary<string, string?> values, CancellationToken ct)
        {
            lock (_lock)
            {
                foreach (var (key, value) in values)
                {
                    if (value is null) _ = _values.Remove((accountKey, key));
                    else _values[(accountKey, key)] = value;
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    /// <summary>The add-on's secrets, in memory.</summary>
    internal sealed class MemorySecrets : IPluginSecrets
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> ReadAsync(string name, CancellationToken ct) => Task.FromResult(_values.TryGetValue(name, out var value) ? value : null);

        public Task WriteAsync(string name, string? value, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(value)) _ = _values.Remove(name);
            else _values[name] = value.Trim();
            return Task.CompletedTask;
        }
    }

    public static AssistantMessage CallsTool(int n) =>
        new("assistant", "step " + n, JsonNode.Parse($$"""[{"id": "c{{n}}", "name": "do_thing", "arguments": { } }]"""), null);

    public static AssistantConfig Config(AssistantActionMode mode) => new(AssistantProviderKind.OpenAiCompatible, "https://api.example.invalid/v1", "model", "English", mode, true, string.Empty, []);
}
