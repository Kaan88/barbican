// Mirrors: see IAssistantSettings; Rust ai.rs:25-65 (Config::load, save), settings.rs:160 (the desktop file is settings.json beside prefs.json). D-M6-A08: one object per account under "assistant" in settings.json; other keys of the file are kept as they are. The API key is not here (IAssistantKeys).

using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Settings.Prefs;
using Barbican.Core.Storage;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantSettings"/>.</summary>
public sealed class AssistantSettings(IProfilePaths paths) : IAssistantSettings
{
    private const string Section = "assistant";

    private readonly Lock _lock = new();
    private JsonObject? _file;
    private DateTime _stamp;

    /// <inheritdoc/>
    public event EventHandler? Changed;

    private string FilePath => Path.Combine(paths.ConfigDir, SettingsStore.DesktopFileName);

    /// <summary>The settings a new account starts with.</summary>
    public static AssistantConfig Defaults { get; } = new(AssistantProviderKind.OpenAiCompatible, "https://api.openai.com/v1", "gpt-4o-mini", "English", AssistantActionMode.Ask, true, string.Empty, []);

    /// <inheritdoc/>
    public AssistantConfig Config(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            return Parse((Load()[Section] as JsonObject)?[accountKey] as JsonObject);
        }
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> SaveAsync(string accountKey, AssistantConfig config, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(config);
        return Task.Run(
            () =>
            {
                try
                {
                    lock (_lock)
                    {
                        var file = Load();
                        var section = file[Section] as JsonObject ?? [];
                        section[accountKey] = ToJson(config);
                        file[Section] = section;
                        AtomicFile.WriteAllText(FilePath, file.ToJsonString());
                        _stamp = File.GetLastWriteTimeUtc(FilePath);
                    }
                }
                catch (IOException e)
                {
                    return Result<Unit>.Fail(new ValidationError("Could not save the assistant settings: " + e.Message));
                }
                catch (UnauthorizedAccessException e)
                {
                    return Result<Unit>.Fail(new ValidationError("Could not save the assistant settings: " + e.Message));
                }

                Changed?.Invoke(this, EventArgs.Empty);
                return Result<Unit>.Ok(default);
            },
            ct);
    }

    private static JsonObject ToJson(AssistantConfig c) => new()
    {
        ["provider"] = (int)c.Provider,
        ["base"] = c.Base,
        ["model"] = c.Model,
        ["language"] = c.Language,
        ["actions"] = (int)c.Actions,
        ["memory"] = c.Memory,
        ["instructions"] = c.Instructions,
        ["notes"] = new JsonArray([.. c.Notes.Select(n => (JsonNode?)n)]),
    };

    private static AssistantConfig Parse(JsonObject? o)
    {
        if (o is null)
        {
            return Defaults;
        }

        return new AssistantConfig(
            Int(o["provider"]) == 1 ? AssistantProviderKind.Anthropic : AssistantProviderKind.OpenAiCompatible,
            Str(o["base"]) ?? Defaults.Base,
            Str(o["model"]) ?? Defaults.Model,
            Str(o["language"]) ?? Defaults.Language,
            Int(o["actions"]) is { } a and >= 0 and <= 2 ? (AssistantActionMode)a : Defaults.Actions,
            o["memory"] is not JsonValue m || !m.TryGetValue<bool>(out var memory) || memory,
            Str(o["instructions"]) ?? string.Empty,
            [.. (o["notes"] as JsonArray ?? []).Select(Str).OfType<string>()]);
    }

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    // the file is kept in memory and read again when its time changes (the import and the reset of the backup replace it)
    private JsonObject Load()
    {
        var stamp = File.GetLastWriteTimeUtc(FilePath);
        if (_file is not null && stamp == _stamp)
        {
            return _file;
        }

        _stamp = stamp;

        try
        {
            _file = AtomicFile.ReadAllTextOrNull(FilePath) is { } text ? JsonNode.Parse(text) as JsonObject : null;
        }
        catch (JsonException)
        {
            _file = null;
        }

        return _file ??= [];
    }
}
