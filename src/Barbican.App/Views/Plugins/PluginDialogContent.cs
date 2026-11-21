// Mirrors Bulwark components/plugins/plugin-dialog-host.tsx:117-160,236-296,306-325,440-515: a host-written "From" line under the title, the message that scrolls, prompt fields
// (required blocks submit, password masks), Cancel and OK, a destructive confirm that starts on Cancel and is not answered by Enter, Escape cancels. Drawn inside the in-window dialog host.
#if WINDOWS
using Barbican.App.Theme;
using Barbican.App.Views.Dialogs;
using Barbican.Core.Plugins.Api.Dialogs;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Barbican.App.Views.Plugins;

internal sealed class PluginDialogContent : IDialogContent
{
    public bool OwnButtons => true;

    public bool CanDraw(object request) => request is PluginDialogHostRequest;

    public UIElement Create(ActiveDialog dialog) => new Form((PluginDialogHostRequest)dialog.Request, dialog).Build();

    private sealed class Form(PluginDialogHostRequest host, ActiveDialog dialog)
    {
        private readonly PluginDialogRequest _request = host.Dialog;
        private readonly List<(PluginDialogField Field, TextBox? Text, PasswordBox? Password)> _inputs = [];
        private Button? _cancel;
        private Button _ok = null!;

        internal StackPanel Build()
        {
            var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
            panel.Children.Add(Text($"From add-on: {host.Source}", 12, "Dim", "plugin.dialog.source"));
            if (_request.Message.Length > 0)
            {
                panel.Children.Add(new ScrollViewer
                {
                    MaxHeight = 260,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = Text(_request.Message, 13, "Body", "plugin.dialog.message"),
                });
            }

            AddFields(panel);
            panel.Children.Add(BuildButtons());
            UpdateEnabled();
            panel.KeyDown += OnKey;
            // the host's own first-focus runs first; this one runs after it
            panel.Loaded += (_, _) => _ = panel.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                Control? target = _inputs.Count > 0 ? (Control?)_inputs[0].Text ?? _inputs[0].Password : _request.Danger ? _cancel : _ok;
                _ = target?.Focus(FocusState.Programmatic);
            });
            return panel;
        }

        private void AddFields(StackPanel panel)
        {
            foreach (var field in _request.Fields)
            {
                var label = field.Label.Length == 0 ? field.Name : field.Label;
                panel.Children.Add(Text(field.Required ? label + " *" : label, 12, "Body", null));
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
                panel.Children.Add(box);
            }

            if (_request.Fields.Any(field => field.Password))
            {
                panel.Children.Add(Text($"{host.Source} receives whatever you type in a password field. Enter nothing you would not give that add-on.", 12, "Dim", null));
            }
        }

        private StackPanel BuildButtons()
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
            if (_request.Kind != PluginDialogKind.Alert)
            {
                _cancel = MakeButton(_request.CancelLabel ?? "Cancel", "Button.Action", "plugin.dialog.cancel");
                _cancel.Click += (_, _) => dialog.Complete(null);
                buttons.Children.Add(_cancel);
            }

            _ok = MakeButton(_request.ConfirmLabel ?? _request.Kind switch { PluginDialogKind.Alert => "OK", PluginDialogKind.Prompt => "Submit", _ => "Confirm" },
                _request.Danger ? "Button.Action" : "Button.Primary", "plugin.dialog.ok");
            if (_request.Danger)
            {
                _ok.Background = AppResources.Brush("RoseBg");
                _ok.Foreground = AppResources.Brush("RoseFg");
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
                CornerRadius = new CornerRadius(14),
                Height = 32,
                Padding = new Thickness(16, 0, 16, 0),
            };
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
        private void OnKey(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                e.Handled = true;
                dialog.Complete(null);
            }
            else if (e.Key == VirtualKey.Enter && e.OriginalSource is not Button && !_request.Danger)
            {
                e.Handled = true;
                Accept();
            }
        }
    }

    private static TextBlock Text(string text, double size, string brush, string? id)
    {
        var block = new TextBlock { Text = text, FontSize = size, Foreground = AppResources.Brush(brush), TextWrapping = TextWrapping.Wrap };
        if (id is not null) AutomationProperties.SetAutomationId(block, id);
        return block;
    }
}
#endif
