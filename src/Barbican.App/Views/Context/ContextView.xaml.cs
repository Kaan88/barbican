using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Mail;

namespace Barbican.App.Views.Context;

/// <summary>
/// The sender panel (prototype ContextTile). It reads the open message from <see cref="ReaderViewModel"/> and the sender's other messages from the rows the
/// list already holds, so it costs no server request; it is rebuilt only when the open message changes.
/// </summary>
public sealed partial class ContextView : UserControl
{
    private const int MaxRecent = 4;
    private readonly MessageListViewModel _list = ViewHost.Get<MessageListViewModel>();
    private string _for = string.Empty;

    public ContextView()
    {
        Reader = ViewHost.Get<ReaderViewModel>();
        Look = ViewHost.Get<StyleState>();
        InitializeComponent();
        Reader.PropertyChanged += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.PropertyName) || string.Equals(e.PropertyName, nameof(ReaderViewModel.EmailId), StringComparison.Ordinal)
                || string.Equals(e.PropertyName, nameof(ReaderViewModel.Header), StringComparison.Ordinal))
            {
                Refresh();
            }
        };
        Refresh();
    }

    public ReaderViewModel Reader { get; }

    public StyleState Look { get; }

    private void Refresh()
    {
        var id = Reader.EmailId;
        var email = Reader.Header.Email;
        Panel.Visibility = string.IsNullOrEmpty(id) ? Visibility.Collapsed : Visibility.Visible;
        var key = id + "\n" + email;
        if (string.Equals(key, _for, StringComparison.Ordinal))
        {
            return;
        }

        _for = key;
        Contacts.ContactsMailSurfaces.FillSender(ContactPanel, email ?? string.Empty);
        var recent = string.IsNullOrEmpty(email)
            ? []
            : _list.Messages
                .Where(r => !string.Equals(r.Id, id, StringComparison.Ordinal)
                            && r.Summary.From.Any(a => string.Equals(a.Email, email, StringComparison.OrdinalIgnoreCase)))
                .Take(MaxRecent)
                .Select(r => new RecentItem(string.IsNullOrEmpty(r.Subject) ? "(no subject)" : r.Subject, r.TimeLabel))
                .ToList();
        RecentList.ItemsSource = recent;
        RecentPanel.Visibility = recent.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
