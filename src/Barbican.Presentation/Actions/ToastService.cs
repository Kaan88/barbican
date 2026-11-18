// Mirrors: Bulwark stores/toast-store.ts, lib/toast-duration.ts (DEFAULT_TOAST_MS 5000, DEFAULT_ERROR_TOAST_MS 10000, the "Notification duration" setting:
// 0 keeps a toast until it is closed), lib/email-action-toast.ts (UNDO_TOAST_DURATION 8000); Rust mail.rs show_toast :1102. Owner actions.
//
// The service only holds the toasts (the host overlay binds to Toasts and raises a Narrator notification for each Announced message): no animation
// decides when a toast leaves, a timer does, and a newer toast of the same text replaces the old one instead of stacking.

using Barbican.Core.Settings.Accessibility;
using Barbican.Core.Storage;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Actions;

/// <summary>The toasts that are up.</summary>
public sealed class ToastService : IToastService, IDisposable
{
    /// <summary>How long a toast with Undo stays (email-action-toast.ts UNDO_TOAST_DURATION).</summary>
    public static readonly TimeSpan UndoDuration = TimeSpan.FromSeconds(8);

    /// <summary>How long an error stays (toast-duration.ts DEFAULT_ERROR_TOAST_MS).</summary>
    public static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(10);

    private const int MaxVisible = 3;

    private readonly IPrefsStore _prefs;
    private readonly TimeProvider _time;
    private readonly IDispatcher _dispatcher;
    private readonly Dictionary<long, ITimer> _timers = [];
    private readonly Lock _lock = new();
    private long _next;

    public ToastService(IPrefsStore prefs, TimeProvider time, IDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(prefs);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _prefs = prefs;
        _time = time;
        _dispatcher = dispatcher;
    }

    /// <summary>A message was shown: the host raises a Narrator notification (RaiseNotificationEvent) with it.</summary>
    public event EventHandler<ToastAnnouncedEventArgs>? Announced;

    /// <summary>The toast shown last, even after it left the screen (the automation toast-last command).</summary>
    public ToastItem? LastShown { get; private set; }

    /// <summary>The toasts on screen, oldest first. Changed in place; the host binds to it.</summary>
    public BulkObservableCollection<ToastItem> Toasts { get; } = [];

    /// <inheritdoc />
    public ToastHandle Show(ToastRequest toast)
    {
        ArgumentNullException.ThrowIfNull(toast);
        var handle = new ToastHandle(Interlocked.Increment(ref _next));

        // the collection is bound to the UI: a send finishing on a pool thread must not touch it there (0x8001010E "marshalled for a different thread", UI-SWEEP-1 C-05)
        if (_dispatcher.HasThreadAccess)
        {
            ShowCore(handle, toast);
        }
        else
        {
            _dispatcher.Post(() => ShowCore(handle, toast));
        }

        return handle;
    }

    private void ShowCore(ToastHandle newHandle, ToastRequest toast)
    {
        ToastItem item;
        lock (_lock)
        {
            // the same words again replace the toast instead of stacking another one
            var same = Toasts.FirstOrDefault(t => string.Equals(t.Message, toast.Message, StringComparison.Ordinal) && t.IsError == toast.IsError);
            if (same is not null)
            {
                DismissLocked(same.Handle);
            }

            item = new ToastItem(newHandle, toast);
            Toasts.Add(item);
            LastShown = item;
            while (Toasts.Count > MaxVisible)
            {
                DismissLocked(Toasts[0].Handle);
            }

            if (DurationOf(toast) is { } after)
            {
                var handle = item.Handle;
                // the collection is bound to the UI: a timer fires on a pool thread, the removal goes to the UI thread
                _timers[handle.Id] = _time.CreateTimer(_ => _dispatcher.Post(() => Dismiss(handle)), null, after, Timeout.InfiniteTimeSpan);
            }
        }

        Announced?.Invoke(this, new ToastAnnouncedEventArgs(item));
    }

    /// <inheritdoc />
    public void Dismiss(ToastHandle handle)
    {
        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.Post(() => Dismiss(handle));
            return;
        }

        lock (_lock)
        {
            DismissLocked(handle);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var t in _timers.Values)
            {
                t.Dispose();
            }

            _timers.Clear();
        }
    }

    /// <summary>The time a toast stays: its own, else the setting (an undo toast at least eight seconds, an error ten); null keeps it until closed.</summary>
    private TimeSpan? DurationOf(ToastRequest toast)
    {
        if (toast.Duration is { } own)
        {
            return own;
        }

        var seconds = A11y.From(_prefs.Current, new NoSystemAccessibility().Current).ToastSecs;
        if (seconds == 0)
        {
            return null;
        }

        var setting = TimeSpan.FromSeconds(seconds);
        var floor = toast.IsError ? ErrorDuration : toast.Action is not null ? UndoDuration : TimeSpan.Zero;
        return setting > floor ? setting : floor;
    }

    private void DismissLocked(ToastHandle handle)
    {
        if (_timers.Remove(handle.Id, out var timer))
        {
            timer.Dispose();
        }

        var at = Toasts.ToList().FindIndex(t => t.Handle == handle);
        if (at >= 0)
        {
            Toasts.RemoveAt(at);
        }
    }
}
