using Barbican.App.Shell;

namespace Barbican.App.Views.Accounts;

/// <summary>The rows of the account popover; a click switches (I-005), "Add account" asks the shell for the sign-in view (I-010).</summary>
public sealed partial class AccountPopoverView : UserControl
{
    public AccountPopoverView()
    {
        Model = ViewHost.Get<AccountPopoverModel>();
        InitializeComponent();
    }

    public AccountPopoverModel Model { get; }

    /// <summary>A row or "Add account" was chosen: the flyout that hosts the popover closes.</summary>
    public event EventHandler? Chosen;

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnRowClick(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            await Model.SwitchAsync(key);
            Chosen?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        Model.RequestAddAccount();
        Chosen?.Invoke(this, EventArgs.Empty);
    }
}
