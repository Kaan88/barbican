// Mirrors: Microsoft WebView2 guidance "Performance: reduce memory usage / suspend a hidden WebView" and API reference:
// CoreWebView2.TrySuspendAsync (suspend only works while the controller IsVisible is false), CoreWebView2.Resume,
// CoreWebView2.MemoryUsageTargetLevel (Low trims the working set of a hidden WebView), WebView2.Close() (releases the browser processes).
// Decision D-WV-01 (spec/decisions.md). Owner body.
//
// When the window is hidden or minimised the body WebView2 is hidden, put on the Low memory target and suspended; after N minutes
// (setting webviewBackgroundCloseMinutes, 0 = never) of being in the background without a break it is closed and re-created on the next
// message open (or when the window returns with a message on screen). Idempotent: every transition checks the state first.
// A generation counter makes a suspend that is still running when the window returns undo itself.

using System.Globalization;
using Barbican.Presentation.Common;

namespace Barbican.Presentation.Mail.Loading;

/// <summary>The policy that releases the mail body web view's resources while the window is in the background.</summary>
public sealed class BodyBackgroundPolicy : IDisposable
{
    /// <summary>The default time in the background before the web view is closed.</summary>
    public const int DefaultCloseMinutes = 5;

    /// <summary>The settings key (minutes; 0 = never close).</summary>
    public const string SettingKey = "webviewBackgroundCloseMinutes";

    private readonly IBodyWebViewHost _host;
    private readonly TimeProvider _time;
    private readonly IDispatcher _dispatcher;
    private ITimer? _timer;
    private long _generation;
    private bool _suspendPending;

    public BodyBackgroundPolicy(IBodyWebViewHost host, TimeProvider time, IDispatcher dispatcher, Func<TimeSpan> closeAfter)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(closeAfter);
        _host = host;
        _time = time;
        _dispatcher = dispatcher;
        CloseAfter = closeAfter;
    }

    /// <summary>The time in the background before the close; zero or less: never. Read when the background starts and when a use restarts the wait.</summary>
    public Func<TimeSpan> CloseAfter { get; set; }

    /// <summary>The window is hidden or minimised.</summary>
    public bool InBackground { get; private set; }

    public BodyPowerState State { get; private set; } = BodyPowerState.Active;

    /// <summary>When the web view was last created again after a close, or null.</summary>
    public DateTimeOffset? LastRecreated { get; private set; }

    public int Suspends { get; private set; }

    public int Closes { get; private set; }

    public int Recreates { get; private set; }

    /// <summary>The setting's value as a time (a missing or bad value is the default, zero or less is never).</summary>
    public static TimeSpan ParseMinutes(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            ? (m > 0 ? TimeSpan.FromMinutes(m) : TimeSpan.Zero)
            : TimeSpan.FromMinutes(DefaultCloseMinutes);

    /// <summary>The window went to the background: hide, lower the memory target, suspend, and start the close wait.</summary>
    public void EnterBackground()
    {
        if (InBackground || !_host.IsSupported)
        {
            return;
        }

        InBackground = true;
        ++_generation;
        StartTimer();
        if (_host.IsCreated && State == BodyPowerState.Active)
        {
            _ = SuspendAsync(_generation);
        }
    }

    /// <summary>The window is back: resume if suspended, restore the memory target, show; a closed web view is re-created when a message is on screen.</summary>
    public void EnterForeground()
    {
        if (!InBackground)
        {
            return;
        }

        InBackground = false;
        ++_generation;
        StopTimer();
        if (State == BodyPowerState.Closed)
        {
            // lazily: only a message on screen needs the web view now; otherwise the next open creates it
            if (_host.HasContent)
            {
                Recreate();
            }

            return;
        }

        if (_host.IsCreated)
        {
            _host.ResumeView();
            _host.Show();
        }

        State = BodyPowerState.Active;
    }

    /// <summary>A message is about to be shown (the body view calls this first): a closed web view is created again, a suspended one resumed.</summary>
    public void BeforeUse()
    {
        switch (State)
        {
            case BodyPowerState.Closed:
                Recreate();
                break;
            case BodyPowerState.Suspended:
                _host.ResumeView();
                State = BodyPowerState.Active;
                break;
            default:
                // still suspending (the call has not returned): the continuation sees the new generation and resumes
                if (_suspendPending)
                {
                    ++_generation;
                    _host.ResumeView();
                }

                break;
        }

        if (InBackground)
        {
            // awake in the background: the close wait starts again from this use
            StartTimer();
        }
    }

    public void Dispose() => StopTimer();

    private void Recreate()
    {
        _host.Recreate();
        State = BodyPowerState.Active;
        LastRecreated = _time.GetUtcNow();
        Recreates++;
        if (InBackground)
        {
            _host.Hide();
        }
    }

    private async Task SuspendAsync(long generation)
    {
        _host.Hide();
        _suspendPending = true;
        bool ok;
        try
        {
            ok = await _host.SuspendAsync().ConfigureAwait(true);
        }
#pragma warning disable CA1031 // a refused suspend leaves the view merely hidden and on the low target
        catch (Exception)
#pragma warning restore CA1031
        {
            ok = false;
        }

        _suspendPending = false;
        if (generation != _generation || State == BodyPowerState.Closed)
        {
            // the window came back (or the view was closed) while the suspend ran
            if (ok && State != BodyPowerState.Closed)
            {
                _host.ResumeView();
            }

            return;
        }

        if (ok)
        {
            State = BodyPowerState.Suspended;
            Suspends++;
        }
    }

    private void StartTimer()
    {
        StopTimer();
        var after = CloseAfter();
        if (after <= TimeSpan.Zero)
        {
            return;
        }

        var generation = _generation;
        _timer = _time.CreateTimer(_ => _dispatcher.Post(() => CloseNow(generation)), null, after, Timeout.InfiniteTimeSpan);
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void CloseNow(long generation)
    {
        if (!InBackground || generation != _generation || State == BodyPowerState.Closed || !_host.IsCreated)
        {
            return;
        }

        _host.Close();
        Closes++;
        State = BodyPowerState.Closed;
    }
}
