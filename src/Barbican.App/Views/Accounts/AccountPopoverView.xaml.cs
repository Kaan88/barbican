// Mirrors: Bulwark components/layout/account-switcher.tsx:453 (a row click switches), :618 (Add account); Fable review 2 R2-03 (bound to
// Presentation's AccountsViewModel; a row of an account the server refused opens the sign-in form for it instead of a switch that cannot work).

using Barbican.App.Shell;
using Barbican.Presentation.Accounts;

namespace Barbican.App.Views.Accounts;

/// <summary>The rows of the account popover; a click switches (I-005), "Add account" asks the shell for the sign-in view (I-010).</summary>
public sealed partial class AccountPopoverView : UserControl
{
    public AccountPopoverView()
    {
        Model = ViewHost.Get<AccountsViewModel>();
        InitializeComponent();
    }

    public AccountsViewModel Model { get; }

    /// <summary>A row or "Add account" was chosen: the flyout that hosts the popover closes.</summary>
    public event EventHandler? Chosen;

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AccountRowViewModel row })
        {
            Chosen?.Invoke(this, EventArgs.Empty);

            // the command reports its own failure in Model.Message, and turns a row that must sign in again into a sign-in request
            Model.SwitchCommand.Execute(row);
        }
    }

    // I-004: Up, Down, Home and End move through the rows and the actions; I-008: Alt+Shift+Up/Down moves the account of the focused row (Bulwark account-switcher.tsx:87, :377, :454).
    // Escape and a click outside close the flyout that hosts the popover (its light dismiss); the keys move focus only inside the window, nothing here sends input.
#pragma warning disable VSTHRD100 // a key handler returns void; a failed move reports itself in Model.Message
    private async void OnPopoverKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        var buttons = new List<Button>();
        Collect(this, buttons);
        var at = buttons.FindIndex(b => ReferenceEquals(b, e.OriginalSource));
        if (at < 0)
        {
            return;
        }

        var key = e.Key;
        if (key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down && IsDown(Windows.System.VirtualKey.Menu) && IsDown(Windows.System.VirtualKey.Shift))
        {
            if (buttons[at].Tag is AccountRowViewModel row)
            {
                e.Handled = true;
                await Model.MoveAsync(row, key == Windows.System.VirtualKey.Up ? -1 : 1);
                buttons[at].Focus(FocusState.Keyboard);
            }

            return;
        }

        var to = key switch
        {
            Windows.System.VirtualKey.Down => Math.Min(at + 1, buttons.Count - 1),
            Windows.System.VirtualKey.Up => Math.Max(at - 1, 0),
            Windows.System.VirtualKey.Home => 0,
            Windows.System.VirtualKey.End => buttons.Count - 1,
            _ => -1,
        };
        if (to >= 0)
        {
            e.Handled = true;
            buttons[to].Focus(FocusState.Keyboard);
        }
    }

    private static bool IsDown(Windows.System.VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // the visible, enabled buttons in tree order: the account rows, Add account and the actions
    private static void Collect(DependencyObject node, List<Button> into)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i);
            if (child is Button { Visibility: Visibility.Visible, IsEnabled: true } button)
            {
                into.Add(button);
            }
            else
            {
                Collect(child, into);
            }
        }
    }

    // the flyout closes at once; the command that follows asks for its confirmation in the dialog host
    private void OnActionClick(object sender, RoutedEventArgs e) => Chosen?.Invoke(this, EventArgs.Empty);

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        Chosen?.Invoke(this, EventArgs.Empty);
        Model.AddAccountCommand.Execute(null);
    }
}
