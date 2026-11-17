#if !WINDOWS
using Barbican.Automation.Capture;

namespace Barbican.Automation.Linux;

/// <summary>
/// Polls a rectangle of the Xvfb root every 8 ms and reports a frame only when the picture changed,
/// so an idle window yields no frames, as on Windows (X11 without a compositor has no frame event).
/// </summary>
internal sealed class X11Poller : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    private X11Poller(WindowRect rect, Action<double, Shot> changed, int everyMs)
    {
        var token = _cts.Token;
        _loop = Task.Run(async () =>
        {
            using var x = new X11Capture();
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(1, everyMs)));
            byte[]? last = null;
            while (!token.IsCancellationRequested)
            {
                var t = AutomationContext.NowMs();
                if (x.Grab(rect.X, rect.Y, rect.Width, rect.Height) is { } shot)
                {
                    if (last is not null && !shot.Bgra.AsSpan().SequenceEqual(last))
                    {
                        changed(t, shot);
                    }

                    last = shot.Bgra;
                }

                try
                {
                    await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, token);
    }

    public static X11Poller Start(WindowRect rect, Action<double, Shot> changed, int everyMs = 8) => new(rect, changed, everyMs);

    /// <summary>Stops polling; the loop ends at its next tick and closes its display connection.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _ = _loop.ContinueWith(_ => _cts.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
#endif
