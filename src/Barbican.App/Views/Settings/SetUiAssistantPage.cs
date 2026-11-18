// Mirrors: B-AI-001, B-AI-002, B-AI-006 (provider kind, base URL, model, language, action mode, memory, standing instructions; the API key lives in the credential store under ai-api-key, never in settings), D-M6-11, D-M6-B03.
// Codex integration: the page reads and writes the same per-account IAssistantSettings used by AssistantSession.

using System.Text.Json.Nodes;
using Barbican.App.Shell;
using Barbican.Core.Settings.Assistant;
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
        var secrets = ViewHost.Get<IAssistantKeys>();
        var settings = ViewHost.Get<IAssistantSettings>();
        var saveGate = new SemaphoreSlim(1, 1);
        var textInputs = new Dictionary<string, TextBox>(StringComparer.Ordinal);
        var pageAccountKey = vm.AccountKey;
        var refreshing = false;
        var form = SetUiUi.Page("AI assistant");
        form.Children.Add(SetUiUi.Meta("Connect a model of your choice. The assistant reads your mail only when you ask, and asks before it changes anything unless you set it to automatic."));

        string Text(string key)
        {
            var config = settings.Config(vm.AccountKey);
            return key switch
            {
                "ai.base" => config.Base,
                "ai.model" => config.Model,
                "ai.lang" => config.Language,
                "ai.instructions" => config.Instructions,
                _ => string.Empty,
            };
        }

        int Number(string key, int fallback)
        {
            var config = settings.Config(vm.AccountKey);
            return key switch { "ai.provider" => (int)config.Provider, "ai.actions" => (int)config.Actions, _ => fallback };
        }

        void Save(string key, JsonNode? value)
        {
            if (refreshing)
            {
                return;
            }

            var accountKey = pageAccountKey;
            SetUiUi.Run(async () =>
            {
                await saveGate.WaitAsync(CancellationToken.None).ConfigureAwait(true);
                try
                {
                    var config = settings.Config(accountKey);
                    var text = value is JsonValue v && v.TryGetValue<string>(out var t) ? t : string.Empty;
                    config = key switch
                    {
                        "ai.provider" => WithProvider(config, (AssistantProviderKind)(value?.GetValue<int>() ?? 0)),
                        "ai.actions" => config with { Actions = (AssistantActionMode)(value?.GetValue<int>() ?? 1) },
                        "ai.base" => config with { Base = text },
                        "ai.model" => config with { Model = text },
                        "ai.lang" => config with { Language = text },
                        "ai.instructions" => config with { Instructions = text },
                        "ai.memory" => config with { Memory = value?.GetValue<bool>() ?? true },
                        _ => config,
                    };
                    var result = await settings.SaveAsync(accountKey, config, CancellationToken.None).ConfigureAwait(true);
                    if (!result.IsOk)
                    {
                        throw new IOException("The assistant settings could not be saved.");
                    }

                    if (string.Equals(key, "ai.provider", StringComparison.Ordinal) && string.Equals(accountKey, pageAccountKey, StringComparison.Ordinal) && textInputs.TryGetValue("ai.base", out var baseBox) && baseBox.FocusState == FocusState.Unfocused)
                    {
                        baseBox.Text = config.Base;
                    }
                }
                finally
                {
                    _ = saveGate.Release();
                }
            });
        }

        var provider = new SetUiChoice(Providers, Math.Clamp(Number("ai.provider", 0), 0, 1), index => Save("ai.provider", JsonValue.Create(index)));
        form.Children.Add(SetUiUi.RowOf("Provider", "OpenAI-compatible servers and Anthropic.", SetUiUi.Id(provider, "set.assistant.provider"), "set.assistant.provider.row"));
        form.Children.Add(TextRow("Base address", "Required. Enter the provider API base address, including /v1.", "ai.base", "https://api.openai.com/v1", Text, Save, textInputs, "set.assistant.base"));
        form.Children.Add(TextRow("Model", "Required. Enter a model supported by this provider.", "ai.model", "gpt-4o-mini", Text, Save, textInputs, "set.assistant.model"));
        form.Children.Add(TextRow("Language", "The language of its answers and of Translate.", "ai.lang", "English", Text, Save, textInputs, "set.assistant.lang"));

        var mode = new SetUiChoice(Modes, Math.Clamp(Number("ai.actions", 1), 0, 2), index => Save("ai.actions", JsonValue.Create(index)));
        form.Children.Add(SetUiUi.RowOf("Actions", "Read only never changes mail. Sending always asks.", SetUiUi.Id(mode, "set.assistant.actions"), "set.assistant.actions.row"));

        var memory = new ToggleSwitch { OnContent = string.Empty, OffContent = string.Empty, IsOn = settings.Config(vm.AccountKey).Memory };
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
            if (key.Password.Length == 0)
            {
                return;
            }

            try
            {
                await secrets.WriteAsync(key.Password, CancellationToken.None).ConfigureAwait(true);
                keyStatus.Text = "Key saved in the credential store.";
            }
            finally
            {
                key.Password = string.Empty;
            }
        }), "set.assistant.key.save"));
        keyButtons.Children.Add(SetUiUi.Pill("Remove key", () => SetUiUi.Run(async () =>
        {
            await secrets.WriteAsync(null, CancellationToken.None).ConfigureAwait(true);
            keyStatus.Text = "Key removed.";
        }), "set.assistant.key.remove"));
        var keyPanel = new StackPanel { Spacing = 6 };
        keyPanel.Children.Add(SetUiUi.Id(key, "set.assistant.key"));
        keyPanel.Children.Add(keyButtons);
        form.Children.Add(SetUiUi.RowOf("API key", "Kept in the credential store, never in your settings or backups.", keyPanel, "set.assistant.key.row"));
        form.Children.Add(keyStatus);

        void Refresh()
        {
            refreshing = true;
            try
            {
                pageAccountKey = vm.AccountKey;
                provider.Selected = Math.Clamp(Number("ai.provider", 0), 0, 1);
                mode.Selected = Math.Clamp(Number("ai.actions", 1), 0, 2);
                memory.IsOn = settings.Config(pageAccountKey).Memory;
                instructions.Text = Text("ai.instructions");
                foreach (var (settingKey, box) in textInputs)
                {
                    box.Text = Text(settingKey);
                }

                key.Password = string.Empty;
                keyStatus.Text = string.Empty;
            }
            finally
            {
                refreshing = false;
            }
        }

        vm.PropertyChanged += (_, e) =>
        {
            if (string.Equals(e.PropertyName, nameof(vm.AccountKey), StringComparison.Ordinal))
            {
                Refresh();
            }
        };

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
            }, Refresh);
    }

    private static AssistantConfig WithProvider(AssistantConfig config, AssistantProviderKind provider)
    {
        const string openAi = "https://api.openai.com/v1";
        const string anthropic = "https://api.anthropic.com/v1";
        var standard = string.Equals(config.Base.TrimEnd('/'), openAi, StringComparison.OrdinalIgnoreCase)
            || string.Equals(config.Base.TrimEnd('/'), anthropic, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(config.Base);
        return config with { Provider = provider, Base = standard ? (provider == AssistantProviderKind.Anthropic ? anthropic : openAi) : config.Base };
    }

    private static Grid TextRow(string label, string description, string key, string placeholder, Func<string, string> read, Action<string, JsonNode?> save, Dictionary<string, TextBox> inputs, string id)
    {
        var box = new TextBox { Text = read(key), PlaceholderText = placeholder, MinWidth = 260 };
        inputs.Add(key, box);
        box.LostFocus += (_, _) => save(key, string.IsNullOrWhiteSpace(box.Text) ? null : JsonValue.Create(box.Text.Trim()));
        return SetUiUi.RowOf(label, description, SetUiUi.Id(box, id), id + ".row");
    }
}
