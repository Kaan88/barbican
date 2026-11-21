// Mirrors: Rust ai.rs:25-65 (Config load and save: provider, base, model, language, actions, memory, instructions), memory.txt (the notes); B-AI-002, B-AI-006, B-SET-039 (per account).
// The values live in the host's per-account add-on settings (IPluginSettings) as text, under the same keys the pre-add-on assistant used ("provider" 0/1, "actions" 0..2, "notes" a JSON array),
// so a profile written before the assistant became an add-on reads unchanged. The API key is not here (IPluginSecrets).

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

/// <summary>The assistant settings of an account over <see cref="IPluginSettings"/>.</summary>
public sealed class AssistantSettingsReader(IPluginSettings settings)
{
    internal const string OpenAiBase = "https://api.openai.com/v1";
    internal const string AnthropicBase = "https://api.anthropic.com/v1";

    /// <summary>The settings a new account starts with.</summary>
    public static AssistantConfig Defaults { get; } = new(AssistantProviderKind.OpenAiCompatible, OpenAiBase, "gpt-4o-mini", "English", AssistantActionMode.Ask, true, string.Empty, []);

    /// <summary>The settings of the account; <see cref="Defaults"/> for whatever is not stored. A memory read.</summary>
    public AssistantConfig Config(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        string? Get(string key) => settings.Read(accountKey, key);
        return new AssistantConfig(
            Number(Get("provider")) == 1 ? AssistantProviderKind.Anthropic : AssistantProviderKind.OpenAiCompatible,
            Get("base") ?? Defaults.Base,
            Get("model") ?? Defaults.Model,
            Get("language") ?? Defaults.Language,
            Number(Get("actions")) is { } a and >= 0 and <= 2 ? (AssistantActionMode)a : Defaults.Actions,
            !bool.TryParse(Get("memory"), out var memory) || memory,
            Get("instructions") ?? string.Empty,
            Notes(Get("notes")));
    }

    /// <summary>Stores the settings of the account.</summary>
    public Task SaveAsync(string accountKey, AssistantConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(config);
        return settings.SetAsync(accountKey, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["provider"] = ((int)config.Provider).ToString(CultureInfo.InvariantCulture),
            ["base"] = config.Base,
            ["model"] = config.Model,
            ["language"] = config.Language,
            ["actions"] = ((int)config.Actions).ToString(CultureInfo.InvariantCulture),
            ["memory"] = config.Memory ? "true" : "false",
            ["instructions"] = config.Instructions,
            ["notes"] = new JsonArray([.. config.Notes.Select(n => (JsonNode?)n)]).ToJsonString(),
        }, ct);
    }

    /// <summary>Changing the provider swaps the standard base address and a recognized old default model (B-AI-001); the extra values to store, or none.</summary>
    public IReadOnlyDictionary<string, string?> AfterChange(string accountKey, string key)
    {
        if (!string.Equals(key, "provider", StringComparison.Ordinal))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        var config = Config(accountKey);
        var trimmed = config.Base.TrimEnd('/');
        var standard = string.Equals(trimmed, OpenAiBase, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, AnthropicBase, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(config.Base);
        var knownModel = config.Model is "gpt-4o-mini" or "claude-haiku-5-5";
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (standard)
        {
            values["base"] = config.Provider == AssistantProviderKind.Anthropic ? AnthropicBase : OpenAiBase;
        }

        if (knownModel)
        {
            values["model"] = string.Empty;
        }

        return values;
    }

    private static int? Number(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string[] Notes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return [.. (JsonNode.Parse(json) as JsonArray ?? []).Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null).OfType<string>()];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
