// Mirrors: see IAssistantSession; Rust agent.rs (per-account history in the profile, memory notes), ai.rs:25-65 (Config); B-AI-006, B-AI-007, B-AI-010, B-AI-011. D-M6-A08: one history file per account in <DataDir>/assistant (named by a hash of the account key: a key is an address); settings in settings.json (IAssistantSettings).
// The history holds the conversation only, never the API key. A question that failed leaves the history as it was before it.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Storage;

namespace Barbican.Core.Settings.Assistant;

/// <summary>The default <see cref="IAssistantSession"/>.</summary>
public sealed class AssistantSession : IAssistantSession
{
    private readonly IAssistantClient _client;
    private readonly IAssistantTools _tools;
    private readonly IAssistantSettings _settings;
    private readonly IAccountManager _accounts;
    private readonly IProfilePaths _paths;
    private readonly TimeProvider _time;
    private readonly IAssistantConfirmation? _confirmation;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<AssistantMessage>> _histories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public AssistantSession(IAssistantClient client, IAssistantTools tools, IAssistantSettings settings, IAccountManager accounts, IProfilePaths paths, TimeProvider time, IAssistantConfirmation? confirmation = null)
    {
        _client = client;
        _tools = tools;
        _settings = settings;
        _accounts = accounts;
        _paths = paths;
        _time = time;
        _confirmation = confirmation;
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public IReadOnlyList<AssistantMessage> History(string accountKey)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        lock (_lock)
        {
            return [.. Loaded(accountKey)];
        }
    }

    /// <inheritdoc/>
    public Task<Result<string>> AskAsync(string accountKey, string question, string? openMessageId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accountKey);
        ArgumentNullException.ThrowIfNull(question);
        return Task.Run(() => AskCoreAsync(accountKey, question.Trim(), openMessageId, ct), ct);
    }

    /// <inheritdoc/>
    public Task<Result<Unit>> ClearAsync(string accountKey, CancellationToken ct)
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

    private async Task<Result<Unit>> ClearCoreAsync(string accountKey, CancellationToken ct)
    {
        var gate = GateOf(accountKey);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_lock)
            {
                Loaded(accountKey).Clear();
                AtomicFile.Delete(PathOf(accountKey));
            }

            Changed?.Invoke(this, EventArgs.Empty);
            return Result<Unit>.Ok(default);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    private string PathOf(string accountKey) =>
        Path.Combine(_paths.DataDir, "assistant", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey)))[..16] + ".json");

    // callers hold _lock
    private List<AssistantMessage> Loaded(string accountKey)
    {
        if (_histories.TryGetValue(accountKey, out var known))
        {
            return known;
        }

        List<AssistantMessage> read = [];
        try
        {
            if (AtomicFile.ReadAllTextOrNull(PathOf(accountKey)) is { } text)
            {
                read = [.. AssistantHistory.FromJson(JsonNode.Parse(text))];
            }
        }
        catch (JsonException)
        {
            // a broken file is an empty conversation; the next answer rewrites it
        }

        _histories[accountKey] = read;
        return read;
    }

    private void Save(string accountKey, List<AssistantMessage> history)
    {
        try
        {
            AtomicFile.WriteAllText(PathOf(accountKey), AssistantHistory.ToJson(history).ToJsonString());
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

    private async Task<Result<string>> AskCoreAsync(string accountKey, string question, string? openMessageId, CancellationToken ct)
    {
        if (question.Length == 0)
        {
            return Result<string>.Fail(new ValidationError("Type a question first."));
        }

        var gate = GateOf(accountKey);

        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var config = _settings.Config(accountKey);
            var user = _accounts.SessionFor(accountKey)?.Client.Session.Username ?? accountKey;
            List<AssistantMessage> working;
            lock (_lock)
            {
                working = [.. Loaded(accountKey)];
            }

            working.Add(new AssistantMessage("user", question, null, null));
            var system = AssistantPrompt.System(config, user, _time.GetUtcNow(), null, openMessageId is { Length: > 0 });
            var client = new SystemPromptClient(_client, system);
            var tools = new OpenMessageTools(_tools, openMessageId, _accounts.SessionFor(accountKey));
            var result = await AssistantLoop.RunAsync(client, tools, config, accountKey, working, (tool, what) => ConfirmAsync(tool, what, ct), _ => Changed?.Invoke(this, EventArgs.Empty), ct).ConfigureAwait(false);
            lock (_lock)
            {
                if (result.IsOk)
                {
                    var history = Loaded(accountKey);
                    history.Clear();
                    history.AddRange(working);
                    Save(accountKey, history);
                }
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
        _confirmation is not null && await _confirmation.ConfirmAsync(tool, what, ct).ConfigureAwait(false);

    // puts the system prompt in front of the conversation (the loop passes the history only)
    private sealed class SystemPromptClient(IAssistantClient inner, string system) : IAssistantClient
    {
        public Task<Result<AssistantMessage>> CompleteAsync(AssistantConfig config, IReadOnlyList<AssistantMessage> messages, IReadOnlyList<AssistantToolSpec> tools, CancellationToken ct) =>
            inner.CompleteAsync(config, [new AssistantMessage("system", system, null, null), .. messages], tools, ct);
    }

    // "current" means the message open in the reader (B-AI-010)
    private sealed class OpenMessageTools(IAssistantTools inner, string? openId, IAccountSession? account) : IAssistantTools
    {
        public IReadOnlyList<AssistantToolSpec> Specs(AssistantActionMode mode) =>
            [.. inner.Specs(mode).Where(s => s.Name switch
            {
                "check_calendar" or "create_event" => account?.Client.Session.Has("urn:ietf:params:jmap:calendars") == true,
                "find_contact" or "create_contact" => account?.Client.Session.Has("urn:ietf:params:jmap:contacts") == true,
                _ => true,
            })];

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
