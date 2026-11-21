// Mirrors: Bulwark stores/notification-center-store.ts:10-119 (the newest 50, newest first; add, markRead, markAllRead, clear; unread count), components/notifications/notification-center.tsx:22 formatBadgeCount ("99+"),
// stores/share-notification-store.ts and calendar-event-notification-store.ts (the share and invitation notifications land here). Kept in memory for the session. Owner notifications.

using System.Globalization;

namespace Barbican.Core.Notifications;

/// <summary>The list behind the bell.</summary>
public sealed class NotificationCenter(TimeProvider time)
{
    /// <summary>How many entries are kept.</summary>
    public const int MaxEntries = 50;

    private readonly Lock _lock = new();
    private List<NotificationEntry> _entries = [];
    private long _next;

    /// <summary>Raised (on any thread) when the list changed.</summary>
    public event EventHandler? Changed;

    /// <summary>The entries, newest first.</summary>
    public IReadOnlyList<NotificationEntry> Entries
    {
        get
        {
            lock (_lock)
            {
                return _entries;
            }
        }
    }

    public int Unread => Entries.Count(e => !e.Read);

    /// <summary>The count on the bell: "" at zero, "99+" from 100.</summary>
    public static string BadgeText(int count) => count <= 0 ? string.Empty : count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);

    public NotificationEntry Add(string level, string title, string body, string source, string? accountKey = null, string? emailId = null)
    {
        ArgumentNullException.ThrowIfNull(title);
        NotificationEntry entry;
        lock (_lock)
        {
            entry = new NotificationEntry(string.Create(CultureInfo.InvariantCulture, $"n{++_next}"), time.GetUtcNow(), level, title, body ?? string.Empty, source, false, accountKey, emailId);
            _entries = [entry, .. _entries.Take(MaxEntries - 1)];
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return entry;
    }

    public void MarkRead(string id) => Update(e => string.Equals(e.Id, id, StringComparison.Ordinal) && !e.Read);

    public void MarkAllRead() => Update(e => !e.Read);

    public void Clear()
    {
        lock (_lock)
        {
            _entries = [];
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Update(Func<NotificationEntry, bool> pick)
    {
        bool changed;
        lock (_lock)
        {
            changed = _entries.Any(pick);
            _entries = [.. _entries.Select(e => pick(e) ? e with { Read = true } : e)];
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
