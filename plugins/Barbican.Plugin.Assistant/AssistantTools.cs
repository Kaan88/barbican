// Mirrors: Rust agent.rs:48-150 (tool specs), :152-440 (exec), B-AI-007, B-AI-008. The mail, calendar, contact, filter, vacation and setting tools run in the host over its typed services (IPluginHostTools); `remember` is the add-on's own (it writes the add-on's settings).

using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

/// <summary>The default <see cref="IAssistantTools"/>: the host's tools plus `remember`.</summary>
public sealed class AssistantTools(IPluginHostTools host, AssistantSettingsReader settings) : IAssistantTools
{
    private const string Remember = "remember";

    private static readonly AssistantToolSpec RememberSpec = new(Remember, "Save a short note about the user's preferences for future chats.", Schema("""{"note": {"type": "string"}}""", "note"), false);

    /// <inheritdoc/>
    public IReadOnlyList<AssistantToolSpec> Specs(string accountKey, AssistantActionMode mode)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        var all = new List<AssistantToolSpec>();
        foreach (var spec in host.Specs(accountKey))
        {
            all.Add(new AssistantToolSpec(spec.Name, spec.Description, JsonNode.Parse(spec.SchemaJson) as JsonObject ?? [], spec.Writes));
        }

        all.Add(RememberSpec);
        return mode == AssistantActionMode.Off ? [.. all.Where(t => !t.Writes)] : all;
    }

    /// <inheritdoc/>
    public async Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(args);
        var text = string.Equals(name, Remember, StringComparison.Ordinal)
            ? await RememberAsync(accountKey, args["note"] is JsonValue v && v.TryGetValue<string>(out var note) ? note : string.Empty, ct).ConfigureAwait(false)
            : await host.RunAsync(accountKey, name, args.ToJsonString(), ct).ConfigureAwait(false);
        return text.Length > AssistantLoop.MaxToolResult ? text[..AssistantLoop.MaxToolResult] : text;
    }

    private static JsonObject Schema(string properties, params string[] required) =>
        new() { ["type"] = "object", ["properties"] = JsonNode.Parse(properties), ["required"] = new JsonArray([.. required.Select(r => (JsonNode?)r)]) };

    private async Task<string> RememberAsync(string accountKey, string note, CancellationToken ct)
    {
        var config = settings.Config(accountKey);
        if (!config.Memory)
        {
            return "Error: Memory is turned off.";
        }

        var clean = note.Trim();
        if (clean.Length == 0)
        {
            return "Error: Nothing to remember.";
        }

        await settings.SaveAsync(accountKey, config with { Notes = [.. config.Notes, clean.Length > 500 ? clean[..500] : clean] }, ct).ConfigureAwait(false);
        return "Noted.";
    }
}
