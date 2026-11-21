// Mirrors: Bulwark components/settings/settings-section.tsx:278-305 (RadioGroup: wrapping row, gap-1.5; each option px-3 py-1.5 text-xs rounded-md; the chosen one bg-primary text-primary-foreground font-medium,
// the others bg-muted hover:bg-accent text-foreground) and :432-460 (Select, drawn the same way here because a WinUI ComboBox opens an unconstrained popup, ADR 0007). ST-04.

using Barbican.App.Controls;
using Barbican.App.Theme;

namespace Barbican.App.Views.Settings;

// In-window choices: WinUI ComboBox opens an unconstrained popup (ADR 0007).
internal sealed class SetUiChoice : Grid
{
    private readonly List<Button> _buttons = [];
    private int _selected;

    public SetUiChoice(IEnumerable<string> labels, int selected, Action<int> changed, string id)
    {
        var look = StyleState.Current;
        var wrap = new WrapRow { Spacing = 6 };
        Children.Add(wrap);
        MaxWidth = 480;
        foreach (var label in labels)
        {
            var index = _buttons.Count;
            var button = new Button
            {
                Content = label,
                Height = look.SetChoiceHeight,
                CornerRadius = look.SetChoiceRadius,
                FontSize = look.SetChoiceSize,
                Padding = new Thickness(12, 0, 12, 0),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(button, id + ".option." + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => { Selected = index; changed(index); };
            _buttons.Add(button);
            wrap.Children.Add(button);
        }

        Selected = selected;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            var bulwark = StyleState.Current.IsBulwark;
            for (var i = 0; i < _buttons.Count; i++)
            {
                var on = i == value;
                var button = _buttons[i];
                button.Style = (Style)Application.Current.Resources[on ? "Button.Primary" : "Button.Action"];
                button.FontWeight = bulwark && on ? Microsoft.UI.Text.FontWeights.Medium : Microsoft.UI.Text.FontWeights.Normal;
                if (bulwark && !on)
                {
                    button.Background = AppResources.Brush("Input");
                    button.BorderThickness = new Thickness(0);
                    button.Foreground = AppResources.Brush("Text");
                }
                else
                {
                    button.ClearValue(Control.BackgroundProperty);
                    button.ClearValue(Control.BorderThicknessProperty);
                    button.ClearValue(Control.ForegroundProperty);
                }
            }
        }
    }
}
