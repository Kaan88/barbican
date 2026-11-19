// Mirrors: Bulwark components/accessibility/accessibility-quick-panel-body.tsx and lib/accessibility-presets.ts (I-417).
using System.Text.Json.Nodes;
using Barbican.App.Shell;
using Barbican.App.Theme;
using Microsoft.UI.Xaml.Data;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Shell;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Settings;

/// <summary>Accessible in-window quick settings, using the shared profile writer.</summary>
public sealed class AccessibilityQuickContent : IDialogContent
{
    public bool OwnButtons => true;
    public double MaxWidth => 560;
    public bool CanDraw(object request) => request is AccessibilityQuickRequest;

    #pragma warning disable MA0051 // The dialog binds one cohesive set of controls and their shared refresh state.
    public UIElement Create(ActiveDialog dialog)
    {
        var model = ((AccessibilityQuickRequest)dialog.Request).Model;
        var look = ViewHost.Get<StyleState>();
        var panel = new StackPanel { Spacing = 10, MinWidth = 300 };
        void Bind(FrameworkElement element, DependencyProperty property, string path) => element.SetBinding(property, new Binding { Source = look, Path = new PropertyPath(path), Mode = BindingMode.OneWay });
        TextBlock Label(string text)
        {
            var label = SetUiUi.Body(text);
            Bind(label, TextBlock.FontSizeProperty, nameof(StyleState.BodyTextSize));
            Bind(label, TextBlock.FontFamilyProperty, nameof(StyleState.AppFont));
            return label;
        }
        Button Action(string text, Action click, string id, bool primary = false)
        {
            var button = SetUiUi.Pill(text, click, id, primary);
            button.Height = double.NaN;
            Bind(button, Control.MinHeightProperty, nameof(StyleState.QuickTarget));
            Bind(button, Control.FontSizeProperty, nameof(StyleState.BodyTextSize));
            Bind(button, Control.FontFamilyProperty, nameof(StyleState.AppFont));
            Bind(button, Control.FocusVisualPrimaryThicknessProperty, nameof(StyleState.FocusThickness));
            return button;
        }
        var content = new StackPanel { Spacing = 10 };
        var status = SetUiUi.Meta(string.Empty);
        var groups = new Dictionary<string, (string Default, List<(string Value, RadioButton Button)> Buttons)>(StringComparer.Ordinal);
        var pending = false;
        var refreshing = false;
        var undo = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        var undoButton = Action("Undo preset", () => { }, "a11y.quick.undo");
        undoButton.IsEnabled = false;

        void Refresh()
        {
            refreshing = true;
            foreach (var (key, group) in groups)
            {
                var value = model.Stored(key)?.ToString() ?? group.Default;
                foreach (var (option, button) in group.Buttons)
                {
                    button.IsChecked = string.Equals(value, option, StringComparison.OrdinalIgnoreCase);
                }
            }
            refreshing = false;
        }

        async Task WriteAsync(string key, JsonNode? value)
        {
            if (pending) { Refresh(); return; }
            pending = true;
            try
            {
                var result = await model.SetAsync(key, value, CancellationToken.None).ConfigureAwait(true);
                status.Text = result.IsOk ? string.Empty : result.Error?.Message ?? "Could not save this setting.";
            }
            finally { pending = false; Refresh(); }
        }

        void Choice(string key, string label, string defaultValue, params (string Value, string Label)[] options)
        {
            var box = new StackPanel { Spacing = 2 };
            box.Children.Add(Label(label));
            var buttons = new List<(string, RadioButton)>();
            StackPanel? row = null;
            foreach (var (value, text) in options)
            {
                if (buttons.Count % 2 == 0) { row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; box.Children.Add(row); }
                var radio = new RadioButton { Content = text, GroupName = "quick." + key, MinHeight = 36 };
                Bind(radio, Control.FontSizeProperty, nameof(StyleState.BodyTextSize));
                Bind(radio, Control.FontFamilyProperty, nameof(StyleState.AppFont));
                Bind(radio, Control.MinHeightProperty, nameof(StyleState.QuickTarget));
                Bind(radio, Control.FocusVisualPrimaryThicknessProperty, nameof(StyleState.FocusThickness));
                AutomationProperties.SetAutomationId(radio, "a11y.quick." + key + "." + value);
                radio.Checked += (_, _) => { if (!refreshing) SetUiUi.Run(() => WriteAsync(key, key is "underlineLinks" or "singleKeyShortcuts" ? JsonValue.Create(string.Equals(value, "true", StringComparison.Ordinal)) : JsonValue.Create(value))); };
                buttons.Add((value, radio));
                row!.Children.Add(radio);
            }
            groups.Add(key, (defaultValue, buttons));
            content.Children.Add(box);
        }

        Choice("fontSize", "Text size", "medium", ("small", "Small"), ("medium", "Medium"), ("large", "Large"), ("x-large", "Extra large"), ("xx-large", "2× large"), ("xxx-large", "3× large"), ("huge", "Huge"));
        Choice("appFont", "Font", "default", ("default", "Default"), ("atkinson", "Atkinson"), ("lexend", "Lexend"), ("opendyslexic", "OpenDyslexic"));
        Choice("contrast", "Contrast", "system", ("system", "System"), ("more", "More"), ("normal", "Normal"));
        Choice("underlineLinks", "Underline links", "false", ("true", "On"), ("false", "Off"));
        Choice("focusRing", "Focus outline", "off", ("off", "Off"), ("normal", "Normal"), ("thick", "Thick"), ("extra", "Extra"));
        Choice("largeTargets", "Large targets", "system", ("system", "System"), ("on", "On"), ("off", "Off"));
        Choice("reduceMotion", "Reduce motion", "system", ("system", "System"), ("on", "On"), ("off", "Off"));
        Choice("singleKeyShortcuts", "Single-key shortcuts", "true", ("true", "On"), ("false", "Off"));
        content.Children.Add(Label("Presets"));
        var presets = new (string Label, string Json)[]
        {
            ("Screen reader", "{\"mailLayout\":\"focus\",\"disableThreading\":true,\"announceNewMail\":true,\"announceRowPreview\":true,\"showRowActionButton\":true}"),
            ("Low vision", "{\"fontSize\":\"xx-large\",\"contrast\":\"more\",\"focusRing\":\"thick\",\"underlineLinks\":true,\"largeCursor\":true,\"mailMinFontSize\":16,\"mailOverrideStyles\":\"high-contrast\",\"mailFitWidth\":true,\"showToolbarLabels\":true}"),
            ("Dyslexia", "{\"appFont\":\"atkinson\",\"lineSpacing\":\"relaxed\",\"letterSpacing\":\"wide\",\"wordSpacing\":\"wide\",\"readingWidth\":\"comfortable\",\"plainTextFont\":\"sans\",\"mailOverrideStyles\":\"always\"}"),
            ("Focus", "{\"simpleMode\":true,\"showPreview\":false,\"senderFavicons\":false,\"unreadCountStyle\":\"dot\",\"reduceMotion\":\"on\",\"pauseAnimatedImages\":\"on\"}"),
            ("Motor", "{\"largeTargets\":\"on\",\"density\":\"comfortable\",\"openOnHover\":false,\"calendarHoverPreview\":\"off\",\"showRowActionButton\":true,\"focusRing\":\"thick\",\"toastDuration\":20,\"confirmDelete\":true,\"nativeScrollbars\":true}"),
            ("Motion", "{\"reduceMotion\":\"on\",\"pauseAnimatedImages\":\"on\",\"reduceTransparency\":\"on\",\"visualAlerts\":false}"),
            ("Hearing", "{\"visualAlerts\":true,\"toastDuration\":0,\"emailNotificationSound\":false,\"calendarNotificationSound\":false}"),
        };
        async Task ApplyAsync(IEnumerable<KeyValuePair<string, JsonNode?>> changes, bool remember)
        {
            if (pending) return;
            pending = true;
            var before = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
            try
            {
                foreach (var (key, value) in changes)
                {
                    var old = model.Stored(key);
                    if (JsonNode.DeepEquals(old, value)) continue;
                    var result = await model.SetAsync(key, value?.DeepClone(), CancellationToken.None).ConfigureAwait(true);
                    if (!result.IsOk) { status.Text = result.Error?.Message ?? "Could not save every setting."; break; }
                    before[key] = old?.DeepClone();
                }
                if (remember) { undo = before; undoButton.IsEnabled = undo.Count > 0; if (status.Text.Length == 0) status.Text = $"{undo.Count} settings changed."; }
                else { undo.Clear(); undoButton.IsEnabled = false; }
            }
            finally { pending = false; Refresh(); }
        }
        StackPanel? presetRow = null;
        for (var i = 0; i < presets.Length; i++)
        {
            if (i % 2 == 0) { presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 }; content.Children.Add(presetRow); }
            var preset = presets[i];
            presetRow!.Children.Add(Action(preset.Label, () => { status.Text = string.Empty; SetUiUi.Run(() => ApplyAsync((JsonObject)JsonNode.Parse(preset.Json)!, true)); }, "a11y.quick.preset." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        undoButton.Click += (_, _) => { status.Text = string.Empty; SetUiUi.Run(() => ApplyAsync(undo.ToArray(), false)); };
        content.Children.Add(undoButton);
        content.Children.Add(status);
        panel.Children.Add(new ScrollViewer { Content = content, MaxHeight = 500, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(Action("All accessibility settings", () => { dialog.Complete(true); var shell = ViewHost.Get<ShellViewModel>(); if (shell.TryActivate(AppId.Settings)) ViewHost.Get<ISettingsNavigation>().Open(SettingsPageIds.Accessibility); }, "a11y.quick.all"));
        actions.Children.Add(Action("Close", () => dialog.Complete(true), "a11y.quick.close", true));
        panel.Children.Add(actions);
        AutomationProperties.SetAutomationId(panel, "a11y.quick");
        Refresh();
        return panel;
    }
    #pragma warning restore MA0051
}
