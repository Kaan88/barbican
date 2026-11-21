// Mirrors: Bulwark components/notifications/notification-center.tsx:43-146 (NotificationPanel: title, "Mark all as read" (disabled at zero unread), "Clear" (disabled when empty), newest first, an unread entry in semibold with a dot, "Nothing here" when empty, source line),
// :176-283 (NotificationBell: the red count badge, "99+", the label "Notifications (N unread)"); formatBadgeCount :22. Owner notifications.

using System.Collections.ObjectModel;
using System.Globalization;
using Barbican.Core.Notifications;
using Barbican.Presentation.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Barbican.Presentation.Notifications;

/// <summary>The bell and the panel behind it.</summary>
public sealed partial class NotificationCenterViewModel : ObservableObject, IDisposable
{
    private readonly NotificationCenter _center;
    private readonly NewMailNotificationService? _service;
    private readonly TimeProvider _time;
    private readonly IDispatcher? _dispatcher;

    public NotificationCenterViewModel(NotificationCenter center, TimeProvider time, NewMailNotificationService? service = null, IDispatcher? dispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(time);
        _center = center;
        _time = time;
        _service = service;
        _dispatcher = dispatcher;
        _center.Changed += OnChanged;
        Refresh();
    }

    /// <summary>The panel's rows, newest first.</summary>
    public ObservableCollection<NotificationRow> Rows { get; } = [];

    public int Unread => _center.Unread;

    /// <summary>The red count on the bell ("" at zero, "99+").</summary>
    public string BadgeText => NotificationCenter.BadgeText(_center.Unread);

    public bool HasUnread => _center.Unread > 0;

    public bool IsEmpty => Rows.Count == 0;

    /// <summary>The bell's accessible name (notification-center.tsx:253).</summary>
    public string BellLabel => Unread > 0 ? string.Create(CultureInfo.InvariantCulture, $"Notifications ({Unread} unread)") : "Notifications";

    public void MarkAllRead() => _center.MarkAllRead();

    public void Clear() => _center.Clear();

    /// <summary>A click on an entry: it is read, and a mail entry opens its message.</summary>
    public void OpenEntry(string id)
    {
        var entry = _center.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (entry is null)
        {
            return;
        }

        _center.MarkRead(id);
        if (string.Equals(entry.Source, "mail", StringComparison.Ordinal))
        {
            _service?.Open(entry.AccountKey, entry.EmailId);
        }
    }

    public void Dispose() => _center.Changed -= OnChanged;

    /// <summary>"just now", "5 min ago", "3 h ago", "2 d ago".</summary>
    public static string Ago(DateTimeOffset then, DateTimeOffset now)
    {
        var span = now - then;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes} min ago")
            : span.TotalDays < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours} h ago")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays} d ago");
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        if (_dispatcher is { HasThreadAccess: false } d)
        {
            d.Post(Refresh);
        }
        else
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var now = _time.GetUtcNow();
        Rows.Clear();
        foreach (var e in _center.Entries)
        {
            Rows.Add(new NotificationRow(e.Id, e.Title, e.Body, e.Source, Ago(e.Time, now), !e.Read, e.Level));
        }

        OnPropertyChanged(nameof(Unread));
        OnPropertyChanged(nameof(BadgeText));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(BellLabel));
    }
}
