// Mirrors: M1-plan §4.3 (apps, unread badge, storage ring, account button); Bulwark components/layout/navigation-rail.tsx; Rust app rail; spec I-001..I-030.
// Owner mailcore. The account popover's content comes from the accounts stream.

using Barbican.Core.Mail.Rules;
using Barbican.Presentation.Mail;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>The app rail: the apps, the mail unread badge, the storage ring and the account button.</summary>
public sealed class RailViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly SidebarViewModel _sidebar;
    private readonly IActiveMail _active;

    public RailViewModel(ShellViewModel shell, SidebarViewModel sidebar, IActiveMail active)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(sidebar);
        ArgumentNullException.ThrowIfNull(active);
        _shell = shell;
        _sidebar = sidebar;
        _active = active;
        _shell.PropertyChanged += OnChildChanged;
        _sidebar.PropertyChanged += OnChildChanged;
        _active.Changed += OnAccountChanged;
    }

    /// <summary>Every app of the rail, in order.</summary>
    public IReadOnlyList<AppId> Apps { get; } = [AppId.Mail, AppId.Calendar, AppId.Contacts, AppId.Files, AppId.Settings];

    public AppId ActiveApp => _shell.ActiveApp;

    public bool ShowAccessibilityButton => _shell.Accessibility.ShowAccessibilityButton;

    /// <summary>Whether an app can be opened yet.</summary>
    public bool IsAvailable(AppId app) => _shell.AvailableApps.Contains(app);

    /// <summary>The Inbox's unread count as the rail shows it ("" at zero, "99+" from 100; hidden by the "hidden" unread style).</summary>
    public string MailBadge
    {
        get
        {
            var unread = _sidebar.InboxUnread;
            if (unread <= 0 || string.Equals(_shell.Accessibility.UnreadStyle, "hidden", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return string.Equals(_shell.Accessibility.UnreadStyle, "dot", StringComparison.Ordinal) ? "●" : unread > 99 ? "99+" : unread.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The storage ring, 0..1, or -1 without a limit.</summary>
    public double StorageFraction => _sidebar.StorageFraction;

    /// <summary>The account button's two letters.</summary>
    public string AccountInitials => RowText.Initials(_active.AccountUser ?? string.Empty);

    /// <summary>The signed-in login (the account button's tooltip).</summary>
    public string AccountLabel => _active.AccountUser ?? string.Empty;

    public void Dispose()
    {
        _shell.PropertyChanged -= OnChildChanged;
        _sidebar.PropertyChanged -= OnChildChanged;
        _active.Changed -= OnAccountChanged;
    }

    private void OnChildChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.ActiveApp):
                OnPropertyChanged(nameof(ActiveApp));
                break;
            case nameof(SidebarViewModel.InboxUnread):
                OnPropertyChanged(nameof(MailBadge));
                break;
            case nameof(SidebarViewModel.StorageFraction):
                OnPropertyChanged(nameof(StorageFraction));
                break;
            case nameof(ShellViewModel.Accessibility):
                OnPropertyChanged(nameof(ShowAccessibilityButton));
                OnPropertyChanged(nameof(MailBadge));
                break;
            default:
                break;
        }
    }

    private void OnAccountChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AccountInitials));
        OnPropertyChanged(nameof(AccountLabel));
    }
}
