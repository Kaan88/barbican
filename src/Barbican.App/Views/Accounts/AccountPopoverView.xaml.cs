// Mirrors: Bulwark components/layout/account-switcher.tsx:353-682 (card, rows, sections), :630-641 (Manage accounts), :453 (a row click switches), :618 (Add account); Fable review 2 R2-03 (bound to
// Presentation's AccountsViewModel; a row of an account the server refused opens the sign-in form for it instead of a switch that cannot work).

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Shell;
using Barbican.Presentation.Accounts;

namespace Barbican.App.Views.Accounts;

/// <summary>The rows of the account popover; a click switches (I-005), "Add account" asks the shell for the sign-in view (I-010).</summary>
public sealed partial class AccountPopoverView : UserControl
{
    public AccountPopoverView()
    {
        Model = ViewHost.Get<AccountsViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Look.PropertyChanged += (_, _) => RestyleRows();
    }

    public AccountsViewModel Model { get; }

    public StyleState Look { get; }

    /// <summary>A row or "Add account" was chosen: the flyout that hosts the popover closes.</summary>
    public event EventHandler? Chosen;

    // A row template cannot bind the style state (x:Bind in a template reads the row), so the metrics that differ between Bulwark and the prototype are applied here:
    // Bulwark rows are square with px-3 py-2.5 and a 36 px avatar, the unread count is plain primary text and the star amber (account-switcher.tsx:459-500).
    private void OnRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Button row)
        {
            Restyle(row);
        }
    }

    private void RestyleRows()
    {
        var buttons = new List<Button>();
        Collect(this, buttons);
        foreach (var button in buttons.Where(b => b.Tag is AccountRowViewModel))
        {
            Restyle(button);
        }
    }

    private void Restyle(Button row)
    {
        row.Padding = Look.PopoverRowPadding;
        row.CornerRadius = Look.PopoverRowRadius;
        if (row.Content is not Grid { Children.Count: 3 } grid)
        {
            return;
        }

        if (grid.Children[0] is Grid { Children: [Border avatar, ..] })
        {
            avatar.Width = avatar.Height = Look.PopoverAvatar;
            avatar.CornerRadius = Look.PopoverAvatarRadius;
        }

        if (grid.Children[1] is StackPanel { Children: [StackPanel { Children: [TextBlock title, Barbican.App.Controls.SIcon star] }, ..] })
        {
            title.FontSize = Look.PopoverTextSize;
            star.Foreground = Look.PopoverStarBrush;
        }

        if (grid.Children[2] is Border { Child: TextBlock text } count)
        {
            count.Background = Look.PopoverCountFill;
            count.Padding = Look.PopoverCountPadding;
            text.Foreground = Look.PopoverCountText;
        }
    }

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

    // Manage accounts: the Settings app (the accounts page lives there)
    private void OnManageClick(object sender, RoutedEventArgs e)
    {
        Chosen?.Invoke(this, EventArgs.Empty);
        ViewHost.Get<ShellViewModel>().TryActivate(AppId.Settings);
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        Chosen?.Invoke(this, EventArgs.Empty);
        Model.AddAccountCommand.Execute(null);
    }
}
