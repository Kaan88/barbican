// Mirrors: B-AI-001, B-AI-002, B-AI-006 (provider kind, base URL, model, language, action mode, memory, standing instructions; the API key lives in the credential store under ai-api-key, never in settings), D-M6-11, D-M6-B03.
// The values are prefs keys of this page (ai.provider, ai.base, ai.model, ai.lang, ai.actions, ai.memory, ai.instructions) until set-data exposes its own config store (D-M6-B05).

using System.Text.Json.Nodes;
using Barbican.App.Shell;
using Barbican.Platform;
using Barbican.Presentation.Settings;

#pragma warning disable MA0051 // a page is built top to bottom in one method, like its layout reads

namespace Barbican.App.Views.Settings;

/// <summary>The AI assistant page.</summary>
internal static class SetUiAssistantPage
{
    private static readonly string[] Providers = ["OpenAI-compatible", "Anthropic"];
    private static readonly string[] Modes = ["Read only", "Ask first", "Automatic"];

    public static SetUiPageFactory Create()
    {
        var vm = SetUiUi.Model;
        var secrets = ViewHost.Find<ISecretStore>();
        var form = SetUiUi.Page("AI assistant");
        form.Children.Add(SetUiUi.Meta("Connect a model of your choice. The assistant reads your mail only when you ask, and asks before it changes anything unless you set it to automatic."));

        string Text(string key) => vm.ValueOf(key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;
        int Number(string key, int fallback) => vm.ValueOf(key) is JsonValue v && v.TryGetValue<int>(out var n) ? n : fallback;
        void Save(string key, JsonNode? value) => SetUiUi.Run(() => vm.SetKeyAsync(key, value, CancellationToken.None));

        var provider = new SetUiChoice(Providers, Math.Clamp(Number("ai.provider", 0), 0, 1), index => Save("ai.provider", JsonValue.Create(index)));
        form.Children.Add(SetUiUi.RowOf("Provider", "OpenAI-compatible servers and Anthropic.", SetUiUi.Id(provider, "set.assistant.provider"), "set.assistant.provider.row"));
        form.Children.Add(TextRow("Base address", "Leave empty for the provider's own.", "ai.base", "https://api.openai.com/v1", Text, Save, "set.assistant.base"));
        form.Children.Add(TextRow("Model", "For example gpt-4o-mini or claude-haiku-5-5.", "ai.model", "gpt-4o-mini", Text, Save, "set.assistant.model"));
        form.Children.Add(TextRow("Language", "The language of its answers and of Translate.", "ai.lang", "English", Text, Save, "set.assistant.lang"));

        var mode = new SetUiChoice(Modes, Math.Clamp(Number("ai.actions", 1), 0, 2), index => Save("ai.actions", JsonValue.Create(index)));
        form.Children.Add(SetUiUi.RowOf("Actions", "Read only never changes mail. Sending always asks.", SetUiUi.Id(mode, "set.assistant.actions"), "set.assistant.actions.row"));

        var memory = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, IsOn = vm.ValueOf("ai.memory") is not JsonValue m || !m.TryGetValue<bool>(out var on) || on };
        memory.Toggled += (_, _) => Save("ai.memory", JsonValue.Create(memory.IsOn));
        form.Children.Add(SetUiUi.RowOf("Remember notes", "Lets the assistant keep short notes between conversations.", SetUiUi.Id(memory, "set.assistant.memory"), "set.assistant.memory.row"));

        var instructions = new TextBox { Text = Text("ai.instructions"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 80, MinWidth = 320 };
        instructions.LostFocus += (_, _) => Save("ai.instructions", string.IsNullOrEmpty(instructions.Text) ? null : JsonValue.Create(instructions.Text));
        form.Children.Add(SetUiUi.RowOf("Standing instructions", "Added to every conversation.", SetUiUi.Id(instructions, "set.assistant.instructions"), "set.assistant.instructions.row"));

        var key = new PasswordBox { MinWidth = 240, PlaceholderText = "API key" };
        var keyStatus = SetUiUi.Meta(string.Empty);
        _ = SetUiUi.Id(keyStatus, "set.assistant.key.status");
        var keyButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        keyButtons.Children.Add(SetUiUi.Pill("Save key", () => SetUiUi.Run(async () =>
        {
            if (secrets is null || key.Password.Length == 0)
            {
                return;
            }

            var r = await secrets.WriteAsync(new SecretName("ai-api-key"), key.Password, CancellationToken.None).ConfigureAwait(true);
            key.Password = string.Empty;
            keyStatus.Text = r.IsOk ? "Key saved in the credential store." : "The key was not saved.";
        }), "set.assistant.key.save"));
        keyButtons.Children.Add(SetUiUi.Pill("Remove key", () => SetUiUi.Run(async () =>
        {
            if (secrets is not null)
            {
                _ = await secrets.DeleteAsync(new SecretName("ai-api-key"), CancellationToken.None).ConfigureAwait(true);
                keyStatus.Text = "Key removed.";
            }
        }), "set.assistant.key.remove"));
        var keyPanel = new StackPanel { Spacing = 6 };
        keyPanel.Children.Add(SetUiUi.Id(key, "set.assistant.key"));
        keyPanel.Children.Add(keyButtons);
        form.Children.Add(SetUiUi.RowOf("API key", "Kept in the credential store, never in your settings or backups.", keyPanel, "set.assistant.key.row"));
        form.Children.Add(keyStatus);

        return new SetUiPageFactory(
            SettingsPageIds.Assistant,
            _ =>
            {
                var root = new Grid { ColumnSpacing = 8 };
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
                root.Children.Add(SetUiUi.Scroll(form));
                var column = new SetUiAssistantColumn { Margin = new Thickness(0, 20, 16, 16), VerticalAlignment = VerticalAlignment.Stretch };
                Grid.SetColumn(column, 1);
                root.Children.Add(column);
                return root;
            });
    }

    private static Grid TextRow(string label, string description, string key, string placeholder, Func<string, string> read, Action<string, JsonNode?> save, string id)
    {
        var box = new TextBox { Text = read(key), PlaceholderText = placeholder, MinWidth = 260 };
        box.LostFocus += (_, _) => save(key, string.IsNullOrWhiteSpace(box.Text) ? null : JsonValue.Create(box.Text.Trim()));
        return SetUiUi.RowOf(label, description, SetUiUi.Id(box, id), id + ".row");
    }
}
