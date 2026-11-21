// Owner notifications. The notifier of builds and tests without an operating system surface (and the fake of the automation).


namespace Barbican.Core.Notifications;

/// <summary>Records what it was asked to show.</summary>
public sealed class NullDesktopNotifier : IDesktopNotifier
{
    private readonly List<NewMailNotice> _shown = [];

    public IReadOnlyList<NewMailNotice> Shown
    {
        get
        {
            lock (_shown)
            {
                return [.. _shown];
            }
        }
    }

    public int Badge { get; private set; }

    public string? LastSound { get; private set; }

    public event EventHandler<NoticeActivatedEventArgs>? Activated;

    public void Show(NewMailNotice notice)
    {
        lock (_shown)
        {
            _shown.Add(notice);
        }
    }

    public void SetBadge(int count) => Badge = count;

    public void PlaySound(string choice) => LastSound = choice;

    /// <summary>Tests: the user clicks the notification.</summary>
    public void Click(NewMailNotice notice) => Activated?.Invoke(this, new NoticeActivatedEventArgs(notice));
}
