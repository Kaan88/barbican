// Bulwark 1.21.2 default theme: the attribution is a 12 px muted line with the Puzzle icon (plugin-dialog-host.tsx:317-322), the message text-sm muted (:324-328), the buttons gap-3 h-10 with a solid
// destructive confirm (:459-466 with ui/button.tsx:52), and the header carries the options button whose menu holds "Turn off <name>" (:583-621). The title is the host's 18 px one.
// Mirrors Bulwark components/plugins/plugin-dialog-host.tsx:117-160,236-296,306-325,440-515: a host-written "From" line under the title, the message that scrolls, prompt fields
// (required blocks submit, password masks), Cancel and OK, a destructive confirm that starts on Cancel and is not answered by Enter, Escape cancels. Drawn inside the in-window dialog host.
#if WINDOWS
using Barbican.App.Theme;
using Barbican.App.Views.Dialogs;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginDialogContent : IDialogContent
{
    private const int MaxBoldDelimiters = 64;

    public bool OwnButtons => true;

    public bool CanDraw(object request) => request is PluginDialogHostRequest;

    public UIElement Create(ActiveDialog dialog) => new Form((PluginDialogHostRequest)dialog.Request, dialog).Build();

    // Bulwark PluginDialogMenu: a vertical-ellipsis button ("Options for <name>") whose menu has one destructive entry, "Turn off <name>"; it disables the add-on, which closes the dialog.
    // The host's XAML declares the button and menu (DialogHostView.xaml).
    public DialogHeaderMenu? CreateHeaderMenu(ActiveDialog dialog)
    {
        var host = (PluginDialogHostRequest)dialog.Request;
        return host.TurnOff is null ? null : new DialogHeaderMenu($"Options for {host.Source}", $"Turn off {host.Source}", () => TurnOffAsync(host, dialog));
    }

#if AUTOMATION
    /// <summary>The form of the dialog on screen, for the synthetic dialog probe (AUTOMATION builds only).</summary>
    internal static Form? Current { get; private set; }
#endif

    private static async Task TurnOffAsync(PluginDialogHostRequest host, ActiveDialog dialog)
    {
        try
        {
            await host.TurnOff!().ConfigureAwait(true);
        }
        finally
        {
            dialog.Complete(null);
        }
    }

    internal sealed class Form(PluginDialogHostRequest host, ActiveDialog dialog)
    {
        private readonly PluginDialogRequest _request = host.Dialog;
        private readonly List<(PluginDialogField Field, TextBox? Text, PasswordBox? Password)> _inputs = [];
        private Button? _cancel;
        private Button _ok = null!;
#if AUTOMATION
        private TextBlock? _source;
#endif

        internal StackPanel Build()
        {
            var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
            var source = Text($"From the add-on {host.Source}", 12, "Dim", "plugin.dialog.source");
#if AUTOMATION
            _source = source;
#endif
            var attribution = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, DialogStyles.Bulwark ? -10 : 0, 0, 0) };
            attribution.Children.Add(AddonGlyph.Create(12, AppResources.Brush("Dim")));
            attribution.Children.Add(source);
            panel.Children.Add(attribution);
            if (_request.Message.Length > 0)
            {
                panel.Children.Add(new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = MessageText(_request.Message),
                });
            }

            AddFields(panel);
            panel.Children.Add(BuildButtons());
            UpdateEnabled();
            panel.KeyDown += OnKey;
#if AUTOMATION
            Current = this;
            panel.Unloaded += (_, _) => { if (ReferenceEquals(Current, this)) Current = null; };
#endif
            // the host's own first-focus runs first; this one runs after it
            panel.Loaded += (_, _) => _ = panel.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                Control? target = _inputs.Count > 0 ? (Control?)_inputs[0].Text ?? _inputs[0].Password : _request.Danger ? _cancel : _ok;
                _ = target?.Focus(FocusState.Programmatic);
            });
            return panel;
        }

        // Bulwark plugin-dialog-host.tsx:440-480: a 14 px gap between the label and the input, 16 px between fields, the password note in a tinted box.
        private void AddFields(StackPanel panel)
        {
            if (_request.Fields.Count == 0) return;
            var fields = new StackPanel { Spacing = 16 };
            foreach (var field in _request.Fields)
            {
                var label = field.Label.Length == 0 ? field.Name : field.Label;
                var group = new StackPanel { Spacing = 4 };
                var title = new TextBlock { FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = AppResources.Brush("Text"), TextWrapping = TextWrapping.Wrap };
                title.Inlines.Add(new Run { Text = label });
                if (field.Required) title.Inlines.Add(new Run { Text = " *", Foreground = AppResources.Brush("RoseFg") });
                group.Children.Add(title);
                Control box;
                if (field.Password)
                {
                    var password = new PasswordBox { PlaceholderText = field.Placeholder ?? string.Empty };
                    password.PasswordChanged += (_, _) => UpdateEnabled();
                    _inputs.Add((field, null, password));
                    box = password;
                }
                else
                {
                    var text = new TextBox { PlaceholderText = field.Placeholder ?? string.Empty };
                    text.TextChanged += (_, _) => UpdateEnabled();
                    _inputs.Add((field, text, null));
                    box = text;
                }

                AutomationProperties.SetAutomationId(box, "plugin.dialog.field." + (_inputs.Count - 1));
                AutomationProperties.SetName(box, label);
                group.Children.Add(box);
                fields.Children.Add(group);
            }

            if (_request.Fields.Any(field => field.Password))
            {
                fields.Children.Add(new Border
                {
                    Background = AppResources.Brush("AmberBg"),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(12, 8, 12, 8),
                    Child = Text($"This form comes from the add-on {host.Source}, not from the mail app. Only enter a password if you trust this add-on.", 12, "Text", "plugin.dialog.password-note"),
                });
            }

            panel.Children.Add(fields);
        }

        private StackPanel BuildButtons()
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = DialogStyles.ButtonGap, Margin = new Thickness(0, DialogStyles.Bulwark ? 12 : 0, 0, 0) };
            if (_request.Kind != PluginDialogKind.Alert)
            {
                _cancel = MakeButton(_request.CancelLabel ?? "Cancel", "Button.Action", "plugin.dialog.cancel");
                _cancel.Click += (_, _) => dialog.Complete(null);
                buttons.Children.Add(_cancel);
            }

            _ok = MakeButton(_request.ConfirmLabel ?? _request.Kind switch { PluginDialogKind.Alert => "OK", PluginDialogKind.Prompt => "Submit", _ => "Confirm" },
                _request.Danger && !DialogStyles.Bulwark ? "Button.Action" : "Button.Primary", "plugin.dialog.ok");
            if (_request.Danger)
            {
                if (DialogStyles.Bulwark)
                {
                    _ok.Style = DialogStyles.Destructive;
                    DialogStyles.Dress(_ok);
                }
                else
                {
                    _ok.Background = AppResources.Brush("RoseBg");
                    _ok.Foreground = AppResources.Brush("RoseFg");
                }
            }

            _ok.Click += (_, _) => Accept();
            buttons.Children.Add(_ok);
            return buttons;
        }

        private static Button MakeButton(string label, string style, string id)
        {
            var button = new Button
            {
                Style = (Style)Application.Current.Resources[style],
                Content = label,
            };
            DialogStyles.Dress(button);
            AutomationProperties.SetAutomationId(button, id);
            return button;
        }

        private static string Value((PluginDialogField Field, TextBox? Text, PasswordBox? Password) input) => input.Text?.Text ?? input.Password?.Password ?? string.Empty;

        private bool CanSubmit() => _inputs.TrueForAll(input => !input.Field.Required || Value(input).Length > 0);

        private void UpdateEnabled() => _ok.IsEnabled = _request.Kind != PluginDialogKind.Prompt || CanSubmit();

        private void Accept()
        {
            if (_request.Kind != PluginDialogKind.Prompt)
            {
                dialog.Complete(PluginDialogOutcome.Accepted());
            }
            else if (CanSubmit())
            {
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var input in _inputs) values[input.Field.Name] = Value(input);
                dialog.Complete(PluginDialogOutcome.Accepted(values));
            }
        }

        // Enter accepts from a field and from the dialog unless the focus is on a button or the confirm is destructive; Escape cancels (a TextBox keeps its keys away from the shell's router).
        private void OnKey(object sender, KeyRoutedEventArgs e) => e.Handled = HandleKey(e.Key, e.OriginalSource);

        private bool HandleKey(VirtualKey key, object? source)
        {
            if (key == VirtualKey.Escape)
            {
                dialog.Complete(null);
                return true;
            }

            if (key == VirtualKey.Enter && source is not Button && !_request.Danger)
            {
                Accept();
                return true;
            }

            return false;
        }

#if AUTOMATION
        internal string Describe()
        {
            var focused = _ok.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) as FrameworkElement : null;
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                title = dialog.Title,
                source = _source?.Text,
                kind = _request.Kind.ToString(),
                danger = _request.Danger,
                message = _request.Message,
                passwordNote = _request.Fields.Any(f => f.Password),
                fields = _inputs.Select(i => new { i.Field.Name, i.Field.Label, i.Field.Password, i.Field.Required, i.Field.Placeholder, length = Value(i).Length }),
                ok = new { label = _ok.Content, enabled = _ok.IsEnabled },
                cancel = _cancel is null ? null : new { label = _cancel.Content, enabled = _cancel.IsEnabled },
                focus = focused is null ? string.Empty : AutomationProperties.GetAutomationId(focused),
            });
        }

        /// <summary>Presses a button the way the automation peer's Invoke does; false when it is missing or disabled.</summary>
        internal bool Press(string which)
        {
            var button = which switch { "ok" => _ok, "cancel" => _cancel, _ => null };
            if (button is null || !button.IsEnabled) return false;
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)).Invoke();
            return true;
        }

        internal bool SetField(int index, string value)
        {
            if (index < 0 || index >= _inputs.Count) return false;
            if (_inputs[index].Text is { } text) text.Text = value;
            else _inputs[index].Password!.Password = value;
            return true;
        }

        /// <summary>Routes a key to the focused element as XAML would: the dialog's handler first, then a focused button's own Enter.</summary>
        internal string Key(VirtualKey key, bool fromNonButton)
        {
            var focused = fromNonButton ? _ok.Parent : _ok.XamlRoot is { } root ? FocusManager.GetFocusedElement(root) : null;
            if (HandleKey(key, focused)) return "handled";
            if (key == VirtualKey.Enter && focused is Button button && button.IsEnabled)
            {
                ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(button)).Invoke();
                return "button";
            }

            return "ignored";
        }
#endif
    }

    // Bulwark plugin-dialog-host.tsx:44-72: text between ** pairs is bold, up to 64 delimiters; the rest shows as written.
    private static TextBlock MessageText(string message)
    {
        var block = new TextBlock { FontSize = DialogStyles.Bulwark ? 14 : 13, Foreground = AppResources.Brush(DialogStyles.Bulwark ? "Dim" : "Body"), TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetAutomationId(block, "plugin.dialog.message");
        var segments = new List<string>();
        var from = 0;
        while (segments.Count < MaxBoldDelimiters)
        {
            var at = message.IndexOf("**", from, StringComparison.Ordinal);
            if (at < 0) break;
            segments.Add(message[from..at]);
            from = at + 2;
        }

        segments.Add(message[from..]);
        for (var i = 0; i < segments.Count; i++)
        {
            if (segments[i].Length == 0) continue;
            var run = new Run { Text = segments[i] };
            if (i % 2 == 1) run.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            block.Inlines.Add(run);
        }

        return block;
    }

    private static TextBlock Text(string text, double size, string brush, string? id)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = AppResources.Brush(brush), TextWrapping = TextWrapping.Wrap };
        if (id is not null) AutomationProperties.SetAutomationId(block, id);
        return block;
    }
}
#endif
