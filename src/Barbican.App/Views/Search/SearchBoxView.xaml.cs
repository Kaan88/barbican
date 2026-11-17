// Mirrors: Bulwark components/search/ (the box); I-023, I-146 (Enter searches, Escape clears). The M1 handler moved here unchanged by stage F of M2; owner search from wave 2.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Search;

/// <summary>The search box of the message list.</summary>
public sealed partial class SearchBoxView : UserControl
{
    private readonly MessageListViewModel _list;

    public SearchBoxView()
    {
        _list = ViewHost.Get<MessageListViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        _list.FocusSearchRequested += (_, _) => SearchBox.Focus(FocusState.Programmatic);
    }

    public StyleState Look { get; }

#pragma warning disable VSTHRD100 // XAML event handlers must return void; the awaited call reports its own failure through the ViewModel
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
#pragma warning restore VSTHRD100
    {
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Enter:
                e.Handled = true;
                await _list.SearchAsync(SearchBox.Text);
                break;
            case global::Windows.System.VirtualKey.Escape:
                e.Handled = true;
                if (SearchBox.Text.Length > 0)
                {
                    SearchBox.Text = string.Empty;
                    await _list.SearchAsync(string.Empty);
                }

                break;
            default:
                break;
        }
    }
}
