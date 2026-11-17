using System.Globalization;
using System.Text;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace S4;

/// <summary>
/// Frame probe: {"frames":[[start_ms,end_ms],...],"events":[[t_ms,"cmd"],...]} since probe-start, the format
/// tools/motion.py reads. Two sources:
///   wgc        (default) every composed frame, from Windows.Graphics.Capture FrameArrived. start = the frame's
///              SystemRelativeTime (when DWM composed it), end = when the app received it. Sees composition-thread
///              animations; idle windows produce no frames.
///   rendering  CompositionTarget.Rendering on the UI thread (start) and a low-priority dispatcher callback (end).
///              The Rust-equivalent, but it only sees UI-thread work and keeps the frame clock ticking while attached.
/// </summary>
internal static class Probe
{
    static readonly object Gate = new();
    static List<double[]> frames = new();
    static List<(double t, string e)> events = new();
    static double t0;
    static bool on;
    static string source = "";
    static WgcSession session;
    static EventHandler<object> rendering;
    static MainWindow win;

    public static string Start(MainWindow w, nint hwnd, string src)
    {
        lock (Gate)
        {
            if (on) return "busy";
            win = w; source = src;
            frames = new(); events = new();
            t0 = Automation.Now();
            on = true;
        }
        if (src == "rendering")
        {
            rendering = (_, _) =>
            {
                var s = Automation.Now() - t0;
                var f = new double[] { s, s };
                lock (Gate) frames.Add(f);
                w.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => f[1] = Automation.Now() - t0);
            };
            CompositionTarget.Rendering += rendering;
        }
        else
        {
            session = new WgcSession(hwnd);
            var first = true;
            session.Frame += (srt, arrive, _) =>
            {
                if (first) { first = false; return; }   // WGC delivers the current picture on start: not a new frame
                lock (Gate) if (on) frames.Add(new[] { srt - t0, Math.Max(srt, arrive) - t0 });
            };
        }
        return "ok";
    }

    public static string Stop()
    {
        lock (Gate) { if (!on) return "{\"frames\":[],\"events\":[]}"; on = false; }
        if (rendering != null) { CompositionTarget.Rendering -= rendering; rendering = null; }
        session?.Dispose(); session = null;
        lock (Gate)
        {
            return JsonSerializer.Serialize(new
            {
                frames = frames.Select(f => new[] { Math.Round(f[0], 2), Math.Round(f[1], 2) }),
                events = events.Select(e => new object[] { Math.Round(e.t, 2), e.e }),
                source,
            });
        }
    }

    public static void Event(string cmd)
    {
        lock (Gate) if (on) events.Add((Automation.Now() - t0, cmd));
        Burst.Event(cmd);
    }
}

/// <summary>`burst MS EVERY DIR [STEP]`: WGC frames of the whole window (WebView2 included), at most one per EVERY ms
/// (0 = all), downscaled by STEP, into DIR as f####_&lt;ms&gt;.png + events.txt + meta.json (tools/flicker.py input).</summary>
internal static class Burst
{
    static readonly object Gate = new();
    static bool running, done;
    static int count;
    static double t0;
    static readonly List<(double t, string e)> events = new();

    public static void Event(string cmd) { lock (Gate) if (running) events.Add((Automation.Now() - t0, cmd)); }
    public static string Status() => running ? "running" : done ? $"done {count} frames" : "none";

    public static string Start(MainWindow w, nint hwnd, string[] a)
    {
        int ms = a.Length > 0 && int.TryParse(a[0], out var m) ? m : 1500;
        int every = a.Length > 1 && int.TryParse(a[1], out var e) ? Math.Max(0, e) : 16;
        var dir = a.Length > 2 ? a[2] : "burst";
        int step = a.Length > 3 && int.TryParse(a[3], out var s) ? Math.Max(1, s) : 2;
        lock (Gate) { if (running) return "busy"; running = true; done = false; events.Clear(); count = 0; }
        Directory.CreateDirectory(dir);
        foreach (var f in Directory.GetFiles(dir)) { var n = Path.GetFileName(f); if (n.EndsWith(".png", StringComparison.Ordinal) || n is "events.txt" or "meta.json") File.Delete(f); }
        var web = (FrameworkElement)((FrameworkElement)w.Content).FindName("Web");
        var o = web.TransformToVisual((UIElement)w.Content).TransformPoint(new Windows.Foundation.Point(0, 0));
        var meta = JsonSerializer.Serialize(new
        {
            ms, every, step, scale = Native.Scale(hwnd),
            body = new[] { o.X, o.Y, web.ActualWidth, web.ActualHeight },
            size = new[] { w.AppWindow.ClientSize.Width, w.AppWindow.ClientSize.Height },
        });
        var shots = new List<(double t, Shot s)>();
        var session = new WgcSession(hwnd);
        t0 = Automation.Now();
        var last = double.NegativeInfinity;
        session.Frame += (_, arrive, f) =>
        {
            var t = arrive - t0;
            if (t < 0 || t - last < every) return;
            last = t;
            var sh = session.Read(f, step);
            lock (shots) shots.Add((t, sh));
        };
        _ = Task.Run(async () =>
        {
            await Task.Delay(ms);
            session.Dispose();
            List<(double t, Shot s)> copy; lock (shots) copy = shots.ToList();
            for (var i = 0; i < copy.Count; i++)
                await Capture.SavePngAsync(copy[i].s, Path.Combine(dir, string.Create(CultureInfo.InvariantCulture, $"f{i:0000}_{copy[i].t:0000000.0}.png")));
            var log = new StringBuilder();
            lock (Gate) foreach (var (t, ev) in events) log.Append(string.Create(CultureInfo.InvariantCulture, $"{t:0.0}\t{ev}\n"));
            File.WriteAllText(Path.Combine(dir, "events.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, "meta.json"), meta);
            lock (Gate) { count = copy.Count; running = false; done = true; }
        });
        return "ok";
    }
}
