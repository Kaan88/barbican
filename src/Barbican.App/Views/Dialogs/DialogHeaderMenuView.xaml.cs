// Mirrors: Bulwark components/plugins/plugin-dialog-host.tsx:583-621 (the add-on dialog's header options button and its one menu entry).

using Barbican.App.Theme;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Dialogs;

/// <summary>A dialog's header options button; the flyout is declared in XAML, this sets its texts and runs the entry.</summary>
public sealed partial class DialogHeaderMenuView : UserControl
{
    private DialogHeaderMenu? _menu;

    public DialogHeaderMenuView() => InitializeComponent();

    /// <summary>Shows the button for this menu, or hides it for null.</summary>
    public void Set(DialogHeaderMenu? menu)
    {
        _menu = menu;
        Visibility = menu is null ? Visibility.Collapsed : Visibility.Visible;
        if (menu is null)
        {
            return;
        }

        AutomationProperties.SetName(MenuButton, menu.ButtonName);
        MenuItem.Text = menu.ItemText;
        MenuButton.CornerRadius = new CornerRadius(StyleState.Current.Metrics.ButtonRadius);
    }

    private void OnItemClick(object sender, RoutedEventArgs e)
    {
        if (_menu is { } menu)
        {
            _ = menu.Invoke();
        }
    }
}
