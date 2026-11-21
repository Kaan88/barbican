// Mirrors: M1-plan §4.3 (apps, unread badge, storage ring, account button); Bulwark components/layout/navigation-rail.tsx; Rust app rail; spec I-001..I-030.
// Owner mailcore. The account popover's content comes from the accounts stream.

using System.Collections.ObjectModel;
using Barbican.Core.Mail.Rules;
using Barbican.Core.Plugins.Pages;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Shell;

/// <summary>The app rail: the apps, the mail unread badge, the storage ring and the account button.</summary>
public sealed class RailViewModel : ObservableObject, IDisposable
{
    private readonly ShellViewModel _shell;
    private readonly SidebarViewModel _sidebar;
    private readonly IActiveMail _active;
    private readonly PluginPageRegistry? _pages;
    private readonly IDispatcher? _dispatcher;

    public RailViewModel(ShellViewModel shell, SidebarViewModel sidebar, IActiveMail active, PluginPageRegistry? pages = null, IDispatcher? dispatcher = null)
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
        _pages = pages;
        _dispatcher = dispatcher;
        if (_pages is not null)
        {
            _pages.Changed += OnPagesChanged;
            SyncPages();
        }
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

    /// <summary>SH-10: the add-on pages after the built-in apps (navigation-rail.tsx:459-512), in the registry's order; empty while no add-on offers one.</summary>
    public ObservableCollection<PluginRailItem> PluginPages { get; } = [];

    /// <summary>An add-on page is on screen: no built-in app of the rail is the current one then.</summary>
    public bool HasActivePluginPage => _shell.ActivePluginPage is not null;

    /// <summary>Shows the add-on page behind a rail entry (a click on it).</summary>
    public void OpenPluginPage(PluginRailItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        _shell.OpenPluginPage(item.Key, string.Empty);
    }

    /// <summary>The account button's two letters.</summary>
    public string AccountInitials => RowText.Initials(_active.AccountUser ?? string.Empty);

    /// <summary>The signed-in login (the account button's tooltip).</summary>
    public string AccountLabel => _active.AccountUser ?? string.Empty;

    public void Dispose()
    {
        _shell.PropertyChanged -= OnChildChanged;
        _sidebar.PropertyChanged -= OnChildChanged;
        _active.Changed -= OnAccountChanged;
        if (_pages is not null)
        {
            _pages.Changed -= OnPagesChanged;
        }
    }

    private void OnPagesChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is not null && !_dispatcher.HasThreadAccess)
        {
            _dispatcher.Post(SyncPages);
        }
        else
        {
            SyncPages();
        }
    }

    // keeps PluginPages equal to the registry (entries of a disabled or removed add-on disappear) and marks the one on screen; leaves the page when its add-on is gone
    private void SyncPages()
    {
        var all = _pages!.All;
        var active = _shell.ActivePluginPage;
        if (active is not null && !all.Any(p => string.Equals(p.Key, active, StringComparison.Ordinal)))
        {
            _shell.ClosePluginPage();
        }

        for (var i = PluginPages.Count - 1; i >= 0; i--)
        {
            if (!all.Any(p => string.Equals(p.Key, PluginPages[i].Key, StringComparison.Ordinal)))
            {
                PluginPages.RemoveAt(i);
            }
        }

        for (var i = 0; i < all.Count; i++)
        {
            var page = all[i];
            var icon = PluginPageIcons.KindFor(page.Page.Icon);
            var existing = PluginPages.FirstOrDefault(p => string.Equals(p.Key, page.Key, StringComparison.Ordinal));
            if (existing is not null && string.Equals(existing.Title, page.Page.Title, StringComparison.Ordinal) && string.Equals(existing.Tooltip, page.Tooltip, StringComparison.Ordinal)
                && string.Equals(existing.IconKind, icon, StringComparison.Ordinal))
            {
                var at = PluginPages.IndexOf(existing);
                if (at != i)
                {
                    PluginPages.Move(at, i);
                }

                continue;
            }

            if (existing is not null)
            {
                PluginPages.Remove(existing);
            }

            PluginPages.Insert(Math.Min(i, PluginPages.Count), new PluginRailItem(page.Key, page.Page.Title, page.Tooltip, icon));
        }

        MarkActivePage();
    }

    private void MarkActivePage()
    {
        var active = _shell.ActivePluginPage;
        foreach (var item in PluginPages)
        {
            item.IsActive = string.Equals(item.Key, active, StringComparison.Ordinal);
        }
    }

    private void OnChildChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.ActiveApp):
                OnPropertyChanged(nameof(ActiveApp));
                break;
            case nameof(ShellViewModel.ActivePluginPage):
                OnPropertyChanged(nameof(HasActivePluginPage));
                MarkActivePage();
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
