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

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnRowClick(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        if (sender is FrameworkElement { Tag: string key })
        {
            await Model.SwitchAsync(key);
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e) => Model.RequestAddAccount();
}
