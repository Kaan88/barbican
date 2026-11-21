// Mirrors: Bulwark lib/desktop/mail-notifications.ts:180-219 startMailNotifications (new mail is gathered for 3 s and told once per account; "Email notifications" off or Do not disturb: nothing; the tone plays once, when "Sound for new mail" is on),
// hooks/use-app-badge.ts:34 and hooks/use-all-inboxes-unread.ts:32 (the taskbar badge is the Inbox unread count plus, with "Count unread mail in all accounts", the other accounts'; "Unread count on the taskbar icon" off clears it), components/notifications/notification-center.tsx (the bell lists what was announced).
// A click on a notification opens the message the way a list row does (the shell's Mail app, IMessageOpener). Owner notifications.

using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Notifications;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;
using Barbican.Presentation.Composition;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Notifications;

/// <summary>Announces new mail through the operating system and the bell, and keeps the taskbar badge.</summary>
public sealed class NewMailNotificationService : IStartupTask, IDisposable
{
    private readonly MailEventHub _hub;
    private readonly IPrefsStore _prefs;
    private readonly INotificationFolderInfo _folders;
    private readonly NotificationCenter _center;
    private readonly IDesktopNotifier _notifier;
    private readonly NewMailNotifier _decider;
    private readonly TimeProvider _time;
    private readonly SidebarViewModel? _sidebar;
    private readonly IUnifiedUnread? _others;
    private readonly ShellViewModel? _shell;
    private readonly IMessageOpener? _opener;
    private readonly IDispatcher? _dispatcher;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<EmailSummary>> _pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EmailSummary> _announced = new(StringComparer.Ordinal);
    private ITimer? _timer;
    private int _badge = -1;

    public NewMailNotificationService(MailEventHub hub, IPrefsStore prefs, INotificationFolderInfo folders, NotificationCenter center, IDesktopNotifier notifier, TimeProvider time,
        SidebarViewModel? sidebar = null, IUnifiedUnread? others = null, ShellViewModel? shell = null, IMessageOpener? opener = null, IDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(time);
        _hub = hub;
        _prefs = prefs;
        _folders = folders;
        _center = center;
        _notifier = notifier;
        _time = time;
        _decider = new NewMailNotifier(time);
        _sidebar = sidebar;
        _others = others;
        _shell = shell;
        _opener = opener;
        _dispatcher = dispatcher;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _hub.EventReceived += OnEvent;
        _notifier.Activated += OnActivated;
        _prefs.Changed += OnPrefsChanged;
        if (_sidebar is not null)
        {
            _sidebar.PropertyChanged += OnSidebarChanged;
        }

        if (_others is not null)
        {
            _others.Changed += OnOthersChanged;
        }

        UpdateBadge();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _hub.EventReceived -= OnEvent;
        _notifier.Activated -= OnActivated;
        _prefs.Changed -= OnPrefsChanged;
        if (_sidebar is not null)
        {
            _sidebar.PropertyChanged -= OnSidebarChanged;
        }

        if (_others is not null)
        {
            _others.Changed -= OnOthersChanged;
        }

        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>The number the taskbar icon shows now (0 when the badge setting is off).</summary>
    public int BadgeCount()
    {
        var settings = NewMailNotificationSettings.From(_prefs.Current);
        if (!settings.BadgeEnabled)
        {
            return 0;
        }

        var own = _sidebar?.InboxUnread ?? 0;
        var others = settings.BadgeAllAccounts && _others is not null ? (int)Math.Min(_others.Total, int.MaxValue / 2) : 0;
        return Math.Max(0, own + others);
    }

    /// <summary>Pushes the badge count to the operating system when it changed.</summary>
    public void UpdateBadge()
    {
        var count = BadgeCount();
        if (Interlocked.Exchange(ref _badge, count) != count)
        {
            _notifier.SetBadge(count);
        }
    }

    /// <summary>Gathers a batch now instead of after the wait (tests).</summary>
    public Task FlushAsync(CancellationToken ct) => DrainAsync(ct);

    /// <summary>Opens what a notification (or a bell entry) stands for: the Mail app, and the message when it is one.</summary>
    public void Open(string? accountKey, string? emailId)
    {
        void Run()
        {
            _ = _shell?.TryActivate(AppId.Mail);
            if (emailId is not null && _opener is not null)
            {
                EmailSummary? summary;
                lock (_lock)
                {
                    _announced.TryGetValue(accountKey + "/" + emailId, out summary);
                }

                if (summary is not null)
                {
                    _opener.Open(new MessageRowViewModel(summary), OpenReason.Search, ListDirection.None);
                }
            }
        }

        if (_dispatcher is { HasThreadAccess: false } d)
        {
            d.Post(Run);
        }
        else
        {
            Run();
        }
    }

    private void OnEvent(object? sender, MailSyncEventArgs e)
    {
        if (e.Event is not NewMailEvent fresh || e.AccountKey is null)
        {
            return;
        }

        lock (_lock)
        {
            if (!_pending.TryGetValue(e.AccountKey, out var list))
            {
                _pending[e.AccountKey] = list = [];
            }

            list.AddRange(fresh.Emails);
            _timer ??= _time.CreateTimer(_ => _ = DrainAsync(CancellationToken.None), null, NewMailNotificationSettings.Batch, Timeout.InfiniteTimeSpan);
            _timer.Change(NewMailNotificationSettings.Batch, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        Dictionary<string, List<EmailSummary>> batch;
        lock (_lock)
        {
            batch = new Dictionary<string, List<EmailSummary>>(_pending, StringComparer.Ordinal);
            _pending.Clear();
        }

        foreach (var (key, mail) in batch)
        {
            var settings = NewMailNotificationSettings.From(_prefs.Current);
            var (roles, muted) = await _folders.GetAsync(key, ct).ConfigureAwait(false);
            if (_decider.Decide(key, mail, roles, muted, settings) is not { } notice)
            {
                continue;
            }

            lock (_lock)
            {
                foreach (var m in mail.TakeLast(50))
                {
                    _announced[key + "/" + m.Id] = m;
                }

                while (_announced.Count > 100)
                {
                    _announced.Remove(_announced.Keys.First());
                }
            }

            _center.Add("info", notice.Title, notice.Body, "mail", key, notice.EmailId);
            _notifier.Show(notice);
        }
    }

    private void OnActivated(object? sender, NoticeActivatedEventArgs e) => Open(e.Notice.AccountKey, e.Notice.EmailId);

    private void OnPrefsChanged(object? sender, EventArgs e) => UpdateBadge();

    private void OnOthersChanged(object? sender, EventArgs e) => UpdateBadge();

    private void OnSidebarChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(SidebarViewModel.InboxUnread), StringComparison.Ordinal))
        {
            UpdateBadge();
        }
    }
}
