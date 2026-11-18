// Scripted collaborators of the assistant loop tests (a client that answers from a list, tools that count their runs). Owner set-data; no other stream's folder uses this name.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Settings.Assistant;

namespace Barbican.Core.Tests.Settings.Assistant;

internal sealed class AssistantTestFakes
{
    internal sealed class ScriptedClient(Func<int, AssistantMessage> script) : IAssistantClient
    {
        public int Calls { get; private set; }

        public Task<Result<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct) =>
            Task.FromResult(Result<AssistantMessage>.Ok(script(++Calls)));
    }

    internal sealed class CountingTools(bool writes, string name = "do_thing") : IAssistantTools
    {
        public int Runs { get; private set; }

        public IReadOnlyList<AssistantToolSpec> Specs(AssistantActionMode mode) => [new(name, "does a thing", [], writes)];

        public Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct)
        {
            Runs++;
            return Task.FromResult("done");
        }
    }

    public static AssistantMessage CallsTool(int n) =>
        new("assistant", "step " + n, JsonNode.Parse($$"""[{"id": "c{{n}}", "name": "do_thing", "arguments": { } }]"""), null);

    public static AssistantConfig Config(AssistantActionMode mode) => new(AssistantProviderKind.OpenAiCompatible, "https://api.example.invalid/v1", "model", "English", mode, true, string.Empty, []);
}
