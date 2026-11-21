// Mirrors: Rust ai.rs and agent.rs (rust-final); spec/behaviours/assistant.md B-AI-001..011; decision D-M6b-08: the assistant is a bundled native add-on and the reference for writing one (README.md).
// Bulwark has no assistant. The add-on declares its settings rows and its chat; the host draws both and supplies everything it needs through PluginContext.

using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

/// <summary>The AI assistant add-on: a chat that reads and, with the user's say-so, changes mail through the host's tools.</summary>
public sealed class AssistantPlugin : IBarbicanPlugin, IPluginSettingsProvider, IPluginChatProvider
{
    private AssistantSettingsReader? _settings;
    private IPluginChat? _chat;

    /// <summary>The add-on's id (the key of its settings and secrets).</summary>
    public const string Id = "assistant";

    /// <inheritdoc />
    public PluginManifest Manifest { get; } = new(1, Id, "AI Assistant", "1.0.0", "A chat that searches and reads your mail, drafts replies and, when you allow it, files and changes mail. Bring your own OpenAI-compatible or Anthropic model.", []);

    /// <inheritdoc />
    public string SettingsTitle => "AI assistant";

    /// <inheritdoc />
    public string SettingsIntro => "Connect a model of your choice. The assistant reads your mail only when you ask, and asks before it changes anything unless you set it to automatic.";

    /// <inheritdoc />
    public IReadOnlyList<PluginSettingRow> SettingRows => AssistantRows.All;

    /// <inheritdoc />
    public IPluginChat Chat => _chat ?? throw new InvalidOperationException("The assistant has not been initialized.");

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string?> AfterSettingChanged(string accountKey, string key) =>
        _settings?.AfterChange(accountKey, key) ?? new Dictionary<string, string?>(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task InitializeAsync(PluginContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        var store = context.Settings ?? throw new InvalidOperationException("The host gave the assistant no settings.");
        var secrets = context.Secrets ?? throw new InvalidOperationException("The host gave the assistant no secret storage.");
        var http = context.Http ?? throw new InvalidOperationException("The host gave the assistant no HTTP client.");
        var tools = context.Tools ?? throw new InvalidOperationException("The host gave the assistant no tools.");
        var dialogs = context.Dialogs;
        _settings = new AssistantSettingsReader(store);
        var session = new AssistantSession(
            new AssistantClient(secrets, http),
            new AssistantTools(tools, _settings),
            _settings,
            store,
            context.DataDirectory,
            TimeProvider.System,
            dialogs is null ? null : (_, what, token) => dialogs.ConfirmAsync("Allow assistant action?", what, token));
        _chat = new AssistantChat(session);
        await store.InitializeAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EmailDetailRow>>([]);
}
