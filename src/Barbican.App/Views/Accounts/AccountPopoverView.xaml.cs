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

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        Chosen?.Invoke(this, EventArgs.Empty);
        Model.AddAccountCommand.Execute(null);
    }
}
