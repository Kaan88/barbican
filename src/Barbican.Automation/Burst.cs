using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Barbican.Automation.Capture;
#if WINDOWS
using Barbican.Automation.Windows;
#else
using Barbican.Automation.Linux;
#endif

namespace Barbican.Automation;

/// <summary>
/// <c>burst MS EVERY DIR [STEP]</c> / <c>burst-status</c> (docs/adr/0007 decision 6; port of
/// spikes/s4-automation/Probe.cs): every composed frame of the window (WGC on Windows; changed Xvfb
/// pictures on Linux), at most one per EVERY ms (0 = all), downscaled by STEP, into DIR as
/// <c>f####_&lt;ms&gt;.png</c>, <c>events.txt</c> and <c>meta.json</c> (tools/flicker.py input).
/// </summary>
internal sealed class Burst
{
    private readonly Lock _gate = new();
    private readonly AutomationContext _context;
    private readonly List<(double T, string E)> _events = [];
    private bool _running;
    private bool _done;
    private int _count;
    private double _t0;

    public Burst(AutomationContext context)
    {
        _context = context;
    }

    public string Status()
    {
        lock (_gate)
        {
            return _running ? "running" : _done ? string.Create(CultureInfo.InvariantCulture, $"done {_count} frames") : "none";
        }
    }

    public void Event(string command)
    {
        lock (_gate)
        {
            if (_running)
            {
                _events.Add((AutomationContext.NowMs() - _t0, command));
            }
        }
    }

    /// <summary>Starts a burst (UI thread: reads the layout for meta.json).</summary>
    public string Start(Protocol.CommandLine line, Func<FrameworkElement?> body)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(body);
        var ms = (int)line.Number(0, 1500);
        var every = Math.Max(0, (int)line.Number(1, 16));
        var dir = line.Token(2, "burst");
        var step = Math.Max(1, (int)line.Number(3, 2));
        lock (_gate)
        {
            if (_running)
            {
                return "busy";
            }

            _running = true;
            _done = false;
            _events.Clear();
            _count = 0;
        }

        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir))
        {
            var n = Path.GetFileName(f);
            if (n.EndsWith(".png", StringComparison.Ordinal) || n is "events.txt" or "meta.json")
            {
                File.Delete(f);
            }
        }

        var root = _context.Root;
        double[] bodyRect = [0, 0, 0, 0];
        if (root is not null && body() is { } web)
        {
            var o = web.TransformToVisual(root).TransformPoint(new global::Windows.Foundation.Point(0, 0));
            bodyRect = [o.X, o.Y, web.ActualWidth, web.ActualHeight];
        }

#if WINDOWS
        var scale = Native.Scale(_context.Hwnd);
#else
        var scale = root?.XamlRoot?.RasterizationScale ?? 1.0;
#endif
        var meta = new JsonObject
        {
            ["ms"] = ms,
            ["every"] = every,
            ["step"] = step,
            ["scale"] = scale,
            ["body"] = new JsonArray(bodyRect.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray()),
            ["size"] = new JsonArray(_context.Window.AppWindow.ClientSize.Width, _context.Window.AppWindow.ClientSize.Height),
        }.ToJsonString();

        var shots = new List<(double T, Shot S)>();
        _t0 = AutomationContext.NowMs();
        var last = double.NegativeInfinity;
#if WINDOWS
        var session = new WgcSession(_context.Hwnd);
        session.Frame += (_, arrive, f) =>
        {
            var t = arrive - _t0;
            if (t < 0 || t - last < every)
            {
                return;
            }

            last = t;
            var sh = session.Read(f, step);
            lock (shots)
            {
                shots.Add((t, sh));
            }
        };
        var feed = session;
#else
        var feed = X11Poller.Start(WindowRect.Of(_context), (arrive, sh) =>
        {
            var t = arrive - _t0;
            if (t < 0 || t - last < every)
            {
                return;
            }

            last = t;
            lock (shots)
            {
                shots.Add((t, Downscale(sh, step)));
            }
        });
#endif
        _ = Task.Run(async () =>
        {
            await Task.Delay(ms).ConfigureAwait(false);
            feed.Dispose();
            List<(double T, Shot S)> copy;
            lock (shots)
            {
                copy = [.. shots];
            }

            for (var i = 0; i < copy.Count; i++)
            {
                PngWriter.Save(copy[i].S, Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"f{i:0000}_{copy[i].T:0000000.0}.png")));
            }

            var log = new StringBuilder();
            lock (_gate)
            {
                foreach (var (t, ev) in _events)
                {
                    log.Append(CultureInfo.InvariantCulture, $"{t:0.0}\t{ev}\n");
                }
            }

            await File.WriteAllTextAsync(Path.Combine(dir, "events.txt"), log.ToString()).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(dir, "meta.json"), meta).ConfigureAwait(false);
            lock (_gate)
            {
                _count = copy.Count;
                _running = false;
                _done = true;
            }
        });
        return "ok";
    }

#if !WINDOWS
    private static Shot Downscale(Shot s, int step)
    {
        if (step <= 1)
        {
            return s;
        }

        int ow = s.Width / step, oh = s.Height / step;
        var o = new byte[ow * oh * 4];
        for (var y = 0; y < oh; y++)
        {
            for (var x = 0; x < ow; x++)
            {
                var src = (y * step * s.Stride) + (x * step * 4);
                var d = ((y * ow) + x) * 4;
                o[d] = s.Bgra[src];
                o[d + 1] = s.Bgra[src + 1];
                o[d + 2] = s.Bgra[src + 2];
                o[d + 3] = 255;
            }
        }

        return new Shot(ow, oh, ow * 4, o);
    }
#endif
}
