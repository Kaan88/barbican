using System.Text.Json.Nodes;
using Barbican.Automation.Capture;
#if WINDOWS
using Barbican.Automation.Windows;
#else
using Barbican.Automation.Linux;
#endif

namespace Barbican.Automation;

/// <summary>
/// <c>probe-start [wgc|rendering|x11]</c> / <c>probe-stop</c> (docs/adr/0007 decision 7; port of
/// spikes/s4-automation/Probe.cs): <c>{"frames":[[start,end],...],"events":[[t,"cmd"],...],"source":...}</c>
/// in ms since probe-start, the format tools/motion.py reads.
/// <list type="bullet">
/// <item><c>wgc</c> (Windows default): one entry per composed frame of the window (start = the DWM
/// present time, end = arrival). Sees composition-thread and WebView animations; idle gives 0 frames.</item>
/// <item><c>x11</c> (Linux default): the window rectangle of the Xvfb root polled every 8 ms; an entry
/// per changed picture (relative numbers only: Xvfb is not vsync-paced).</item>
/// <item><c>rendering</c>: CompositionTarget.Rendering. Attaching it makes the UI thread tick at the
/// display rate even when nothing changes: for UI-thread cost only, never for the idle gate.</item>
/// </list>
/// </summary>
internal sealed class FrameProbe
{
    private readonly Lock _gate = new();
    private readonly AutomationContext _context;
    private List<double[]> _frames = [];
    private List<(double T, string E)> _events = [];
    private double _t0;
    private bool _on;
    private string _source = string.Empty;
    private EventHandler<object>? _rendering;
#if WINDOWS
    private WgcSession? _feed;
#else
    private X11Poller? _feed;
#endif

    public FrameProbe(AutomationContext context)
    {
        _context = context;
    }

    public static string DefaultSource => OperatingSystem.IsWindows() ? "wgc" : "x11";

    public string Start(string source)
    {
        lock (_gate)
        {
            if (_on)
            {
                return "busy";
            }

            _source = string.IsNullOrEmpty(source) ? DefaultSource : source;
            _frames = [];
            _events = [];
            _t0 = AutomationContext.NowMs();
            _on = true;
        }

        switch (_source)
        {
            case "rendering":
                _rendering = (_, _) =>
                {
                    var s = AutomationContext.NowMs() - _t0;
                    var f = new[] { s, s };
                    lock (_gate)
                    {
                        _frames.Add(f);
                    }

                    _ = _context.Dispatcher.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => f[1] = AutomationContext.NowMs() - _t0);
                };
                CompositionTarget.Rendering += _rendering;
                return "ok";
#if WINDOWS
            case "wgc":
            {
                var session = new WgcSession(_context.Hwnd);
                var first = true;
                session.Frame += (srt, arrive, _) =>
                {
                    if (first)
                    {
                        first = false;   // WGC delivers the current picture on start: not a new frame
                        return;
                    }

                    lock (_gate)
                    {
                        if (_on)
                        {
                            _frames.Add([srt - _t0, Math.Max(srt, arrive) - _t0]);
                        }
                    }
                };
                _feed = session;
                return "ok";
            }
#else
            case "x11":
            {
                var rect = WindowRect.Of(_context);
                _feed = X11Poller.Start(rect, (t, _) =>
                {
                    lock (_gate)
                    {
                        if (_on)
                        {
                            _frames.Add([t - _t0, t - _t0]);
                        }
                    }
                });
                return "ok";
            }
#endif
            default:
                lock (_gate)
                {
                    _on = false;
                }

                return "error unknown probe source " + _source;
        }
    }

    public string Stop()
    {
        lock (_gate)
        {
            if (!_on)
            {
                return "{\"frames\":[],\"events\":[]}";
            }

            _on = false;
        }

        if (_rendering is not null)
        {
            CompositionTarget.Rendering -= _rendering;
            _rendering = null;
        }

        _feed?.Dispose();
        _feed = null;
        lock (_gate)
        {
            var frames = new JsonArray();
            foreach (var f in _frames)
            {
                frames.Add(new JsonArray(Math.Round(f[0], 2), Math.Round(f[1], 2)));
            }

            var events = new JsonArray();
            foreach (var (t, e) in _events)
            {
                events.Add(new JsonArray(Math.Round(t, 2), e));
            }

            return new JsonObject { ["frames"] = frames, ["events"] = events, ["source"] = _source }.ToJsonString();
        }
    }

    public void Event(string command)
    {
        lock (_gate)
        {
            if (_on)
            {
                _events.Add((AutomationContext.NowMs() - _t0, command));
            }
        }
    }

    /// <summary>Saves one picture of the window (WGC on Windows, the Xvfb root rectangle on Linux).</summary>
    public static async Task<string> WindowShotAsync(AutomationContext context, string path)
    {
#if WINDOWS
        var tcs = new TaskCompletionSource<Shot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (var s = new WgcSession(context.Hwnd))
        {
            s.Frame += (_, _, f) =>
            {
                if (tcs.Task.IsCompleted)
                {
                    return;
                }

                try
                {
                    tcs.TrySetResult(s.Read(f));
                }
#pragma warning disable CA1031 // reported as the command's error
                catch (Exception e)
#pragma warning restore CA1031
                {
                    tcs.TrySetException(e);
                }
            };
            var done = await Task.WhenAny(tcs.Task, Task.Delay(3000));
            if (done != tcs.Task || await tcs.Task is not { } shot)
            {
                return "error capture failed";
            }

            await Task.Run(() => PngWriter.Save(shot, path));
            return $"saved {shot.Width}x{shot.Height}";
        }
#else
        var rect = WindowRect.Of(context);
        using var x = new X11Capture();
        var shot = x.Grab(rect.X, rect.Y, rect.Width, rect.Height);
        if (shot is null)
        {
            return "error capture failed";
        }

        await Task.Run(() => PngWriter.Save(shot, path));
        return $"saved {shot.Width}x{shot.Height}";
#endif
    }
}
