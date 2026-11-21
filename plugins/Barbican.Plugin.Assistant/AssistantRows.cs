// Mirrors: B-AI-001, B-AI-002, B-AI-006 (provider kind, base URL, model, language, action mode, memory, standing instructions; the API key lives in the credential store, never in settings), D-M6-11.
// The rows the host draws for this add-on with its generic settings rows (IPluginSettingsProvider).

using Barbican.Plugins;

namespace Barbican.Plugin.Assistant;

internal static class AssistantRows
{
    internal static readonly IReadOnlyList<PluginSettingRow> All =
    [
        new("provider", "Provider", "OpenAI-compatible servers and Anthropic.", PluginSettingKind.Choice,
            [new PluginChoice("0", "OpenAI-compatible"), new PluginChoice("1", "Anthropic")], Default: "0"),
        new("base", "Base address", "Required. Enter the provider API base address, including /v1.", PluginSettingKind.Text, Placeholder: "https://api.openai.com/v1", Default: AssistantSettingsReader.OpenAiBase),
        new("model", "Model", "Required. Enter a model supported by this provider.", PluginSettingKind.Text, Placeholder: "Enter a model ID", Default: "gpt-4o-mini"),
        new("language", "Language", "The language of its answers and of Translate.", PluginSettingKind.Text, Placeholder: "English", Default: "English"),
        new("actions", "Actions", "Read only never changes mail. Sending always asks.", PluginSettingKind.Choice,
            [new PluginChoice("0", "Read only"), new PluginChoice("1", "Ask first"), new PluginChoice("2", "Automatic")], Default: "1"),
        new("memory", "Remember notes", "Lets the assistant keep short notes between conversations.", PluginSettingKind.Toggle, Default: "true"),
        new("instructions", "Standing instructions", "Added to every conversation.", PluginSettingKind.MultilineText),
        new(AssistantClient.ApiKeyName, "API key", "Kept in the credential store, never in your settings or backups.", PluginSettingKind.Secret, Placeholder: "API key"),
    ];
}
