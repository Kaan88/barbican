// Mirrors: Rust assistant column (B-AI-006 the conversation and Clear, B-AI-010 "this email" is the open message). The add-on side of the host's generic chat panel (IPluginChat); the panel draws, the session thinks.

using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

/// <summary>The assistant conversation as the host's chat panel sees it.</summary>
public sealed class AssistantChat : IPluginChat
{
    private readonly AssistantSession _session;

    /// <summary>Wraps a session.</summary>
    public AssistantChat(AssistantSession session)
    {
        _session = session;
        _session.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc/>
    public event EventHandler? Changed;

    /// <inheritdoc/>
    public string Title => "Assistant";

    /// <inheritdoc/>
    public string InputPlaceholder => "Ask about your mail";

    /// <inheritdoc/>
    public string EmptyHint => "Ask a question. Select a message in your list first to ask about it.";

    /// <inheritdoc/>
    public Task InitializeAsync(string accountKey, CancellationToken ct) => _session.InitializeHistoryAsync(accountKey, ct);

    /// <inheritdoc/>
    public IReadOnlyList<PluginChatTurn> Turns(string accountKey) =>
        [.. _session.History(accountKey)
            .Where(m => m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Text))
            .Select(m => new PluginChatTurn(string.Equals(m.Role, "user", StringComparison.Ordinal), m.Text))];

    /// <inheritdoc/>
    public async Task<PluginChatReply> SendAsync(string accountKey, string text, string? openMessageId, CancellationToken ct)
    {
        var result = await _session.AskAsync(accountKey, text, openMessageId, ct).ConfigureAwait(false);
        return result.IsOk ? PluginChatReply.Answer(result.Value!) : PluginChatReply.Failure(result.Error ?? "The assistant did not answer.");
    }

    /// <inheritdoc/>
    public Task ClearAsync(string accountKey, CancellationToken ct) => _session.ClearAsync(accountKey, ct);
}
