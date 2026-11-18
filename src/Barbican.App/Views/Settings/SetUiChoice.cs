namespace Barbican.App.Views.Settings;

// In-window choices: WinUI ComboBox opens an unconstrained popup (ADR 0007).
internal sealed class SetUiChoice : StackPanel
{
    private readonly List<Button> _buttons = [];
    private int _selected;

    public SetUiChoice(IEnumerable<string> labels, int selected, Action<int> changed)
    {
        Spacing = 6;
        StackPanel? row = null;
        foreach (var label in labels)
        {
            var index = _buttons.Count;
            if (index % 3 == 0)
            {
                row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                Children.Add(row);
            }

            var button = new Button { Content = label, Height = 30, CornerRadius = new CornerRadius(15), FontSize = 12.5, Padding = new Thickness(12, 0, 12, 0) };
            button.Click += (_, _) => { Selected = index; changed(index); };
            _buttons.Add(button);
            row!.Children.Add(button);
        }

        Selected = selected;
    }

    public int Selected
    {
        get => _selected;
        set
        {
            _selected = value;
            for (var i = 0; i < _buttons.Count; i++)
            {
                _buttons[i].Style = (Style)Application.Current.Resources[i == value ? "Button.Primary" : "Button.Action"];
            }
        }
    }
}
