// Mirrors: Rust agent.rs (per-account history, memory notes), ai.rs:25-65 (Config); B-AI-006, B-AI-007, B-AI-010, B-AI-011. One history file per account in the add-on's data folder (named by a hash of the account key: a key is an address).
// The history holds the conversation only, never the API key. A question that failed leaves the history as it was before it. THREADING: AskAsync runs off the UI thread; Changed (history grew, a tool ran, progress text) is raised on a pool thread.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

/// <summary>One add-on conversation per account: history, the agent loop over the client and the tools, confirmation of writes.</summary>
public sealed class AssistantSession
{
    private readonly IAssistantClient _client;
    private readonly IAssistantTools _tools;
    private readonly AssistantSettingsReader _settings;
    private readonly IPluginSettings _store;
    private readonly string _dataDirectory;
    private readonly TimeProvider _time;
    private readonly Func<string, string, CancellationToken, Task<bool>>? _confirm;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<AssistantMessage>> _histories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    /// <param name="client">The model.</param>
    /// <param name="tools">What the model may call.</param>
    /// <param name="settings">The account's settings.</param>
    /// <param name="store">The host settings (loaded before the first question).</param>
    /// <param name="dataDirectory">Where the history files go.</param>
    /// <param name="time">The clock of the system prompt.</param>
    /// <param name="confirm">Asks the user before a write (tool name, what it would do); null declines every write, the safe default.</param>
    public AssistantSession(IAssistantClient client, IAssistantTools tools, AssistantSettingsReader settings, IPluginSettings store, string dataDirectory, TimeProvider time, Func<string, string, CancellationToken, Task<bool>>? confirm = null)
    {
        _client = client;
        _tools = tools;
        _settings = settings;
        _store = store;
        _dataDirectory = dataDirectory;
        _time = time;
        _confirm = confirm;
    }

    /// <summary>Raised when the history changed or a progress line arrived, on a pool thread.</summary>
    public event EventHandler? Changed;

    /// <summary>The conversation so far (a memory-only snapshot, empty until loaded).</summary>
    public IReadOnlyList<AssistantMessage> History(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            return _histories.TryGetValue(accountKey, out var history) ? [.. history] : [];
        }
    }

    /// <summary>Loads this account's saved history off the UI thread.</summary>
    public Task InitializeHistoryAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        return Task.Run(() => InitializeHistoryCoreAsync(accountKey, ct), ct);
    }

    private async Task InitializeHistoryCoreAsync(string accountKey, CancellationToken ct)
    {
        var gate = GateOf(accountKey);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        bool loaded;
        try
        {
            loaded = EnsureLoaded(accountKey);
        }
        finally
        {
            _ = gate.Release();
        }

        if (loaded) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks a question; <c>openMessageId</c> is "this email" (id <c>current</c>, B-AI-010). Returns the answer text; the history is saved.</summary>
    public Task<AssistantResult<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(question);
        return Task.Run(() => AskCoreAsync(accountKey, question.Trim(), openMessageId, ct), ct);
    }

    /// <summary>Forgets the conversation of the account.</summary>
    public Task ClearAsync(string accountKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        return Task.Run(() => ClearCoreAsync(accountKey, ct), ct);
    }

    private SemaphoreSlim GateOf(string accountKey)
    {
        lock (_lock)
        {
            if (!_gates.TryGetValue(accountKey, out var gate))
            {
                gate = _gates[accountKey] = new SemaphoreSlim(1, 1);
            }

            return gate;
        }
    }

    private async Task ClearCoreAsync(string accountKey, CancellationToken ct)
    {
        var gate = GateOf(accountKey);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            File.Delete(PathOf(accountKey));
            lock (_lock)
            {
                _histories[accountKey] = [];
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private string PathOf(string accountKey) =>
        Path.Combine(_dataDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey)))[..16] + ".json");

    // Caller holds the per-account gate; disk IO is never under the cache lock.
    private bool EnsureLoaded(string accountKey)
    {
        lock (_lock)
        {
            if (_histories.ContainsKey(accountKey)) return false;
        }

        List<AssistantMessage> read = [];
        try
        {
            if (File.Exists(PathOf(accountKey)) && File.ReadAllText(PathOf(accountKey)) is { } text)
            {
                read = [.. AssistantHistory.FromJson(JsonNode.Parse(text))];
            }
        }
        catch (JsonException)
        {
            // A broken file is an empty conversation; the next answer rewrites it.
        }

        lock (_lock) { _histories[accountKey] = read; }
        return true;
    }

    private void Save(string accountKey, List<AssistantMessage> history)
    {
        try
        {
            var path = PathOf(accountKey);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, AssistantHistory.ToJson(history).ToJsonString());
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException)
        {
            // the chat still works; it just will not survive a restart
        }
        catch (UnauthorizedAccessException)
        {
            // as above
        }
    }

    private async Task<AssistantResult<string>> AskCoreAsync(string accountKey, string question, string? openMessageId, CancellationToken ct)
    {
        if (question.Length == 0)
        {
            return AssistantResult<string>.Fail("Type a question first.");
        }

        var gate = GateOf(accountKey);

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _store.InitializeAsync(ct).ConfigureAwait(false);
            var config = _settings.Config(accountKey);
            _ = EnsureLoaded(accountKey);
            List<AssistantMessage> working;
            lock (_lock)
            {
                working = [.. _histories[accountKey]];
            }

            working.Add(new AssistantMessage("user", question, null, null));
            var system = AssistantPrompt.System(config, accountKey, _time.GetUtcNow(), null, openMessageId is { Length: > 0 });
            var client = new SystemPromptClient(_client, system);
            var tools = new OpenMessageTools(_tools, openMessageId);
            var result = await AssistantLoop.RunAsync(client, tools, config, accountKey, working, (tool, what) => ConfirmAsync(tool, what, ct), _ => Changed?.Invoke(this, EventArgs.Empty), ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                Save(accountKey, working);
                lock (_lock) { _histories[accountKey] = working; }
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private async Task<bool> ConfirmAsync(string tool, string what, CancellationToken ct) =>
        _confirm is not null && await _confirm(tool, what, ct).ConfigureAwait(false);

    // puts the system prompt in front of the conversation (the loop passes the history only)
    private sealed class SystemPromptClient(IAssistantClient inner, string system) : IAssistantClient
    {
        public Task<AssistantResult<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct) =>
            inner.CompleteAsync(config, [new AssistantMessage("system", system, null, null), .. messages], tools, ct);
    }

    // "current" means the message open in the reader (B-AI-010)
    private sealed class OpenMessageTools(IAssistantTools inner, string? openId) : IAssistantTools
    {
        public IReadOnlyList<AssistantToolSpec> Specs(string accountKey, AssistantActionMode mode) => inner.Specs(accountKey, mode);

        public Task<string> RunAsync(string accountKey, string name, JsonObject args, CancellationToken ct)
        {
            var open = openId ?? string.Empty;
            if (args["id"] is JsonValue id && id.TryGetValue<string>(out var s) && (s.Length == 0 || string.Equals(s, "current", StringComparison.Ordinal)))
            {
                args["id"] = open;
            }

            if (args["reply_to_id"] is JsonValue reply && reply.TryGetValue<string>(out var replyId) && string.Equals(replyId, "current", StringComparison.Ordinal))
            {
                args["reply_to_id"] = open;
            }

            if (args["ids"] is JsonArray ids)
            {
                for (var i = 0; i < ids.Count; i++)
                {
                    if (ids[i] is JsonValue v && v.TryGetValue<string>(out var x) && string.Equals(x, "current", StringComparison.Ordinal))
                    {
                        ids[i] = open;
                    }
                }
            }

            return inner.RunAsync(accountKey, name, args, ct);
        }
    }
}
