using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Barbican.Spike.BodyShell.Capture;
using Windows.Foundation;
using Windows.UI;

namespace Barbican.Spike.BodyShell;

/// <summary>
/// The S2 measurement script. Every probe is in-process: commands go to the WebView2 through its
/// API and to XAML directly; nothing uses the real mouse or keyboard. Frames come from a capture of
/// the spike's own window (Windows) or X display (Linux, Xvfb).
/// </summary>
internal sealed partial class Scenario
{
    private const string ColorA = "#3366cc";
    private const string ColorB = "#cc6633";
    private const string Magenta = "#ff00ff";

    private static readonly Color A = Color.FromArgb(255, 0x33, 0x66, 0xCC);
    private static readonly Color B = Color.FromArgb(255, 0xCC, 0x66, 0x33);
    private static readonly Color MagentaColor = Color.FromArgb(255, 0xFF, 0x00, 0xFF);
    private static readonly Color PopupColor = Color.FromArgb(255, 0x1C, 0x1C, 0x1C);

    private readonly SpikeRoot _root;
    private readonly Window _window;
    private readonly string _outDir;
    private readonly SpikeReport _report = new();
    private readonly List<string> _messages = [];
    private ShellHost _shell = null!;
    private IFrameSource? _capture;
    private string _darkJs = string.Empty;

    public Scenario(SpikeRoot root, Window window, string outDir)
    {
        _root = root;
        _window = window;
        _outDir = outDir;
    }

    public async Task<int> RunAsync()
    {
        _report.StartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        _report.Platform = RuntimeInformation.OSDescription;
#if WINDOWS
        _report.Head = "WinUI 3 (net10.0-windows)";
#else
        _report.Head = "Uno Skia desktop (net10.0-desktop)";
#endif
        try
        {
            await RunCoreAsync();
        }
        catch (Exception e)
        {
            _report.Notes.Add("CRASH " + e);
            Log("crash: " + e);
        }
        finally
        {
            _capture?.Dispose();
        }

        var json = JsonSerializer.Serialize(_report, ShellJson.Default.SpikeReport);
        await File.WriteAllTextAsync(Path.Combine(_outDir, "report.json"), json);
        var ok = _report.Checks.TrueForAll(c => c.Pass) && _report.Phases.TrueForAll(p => p.Pass);
        Log($"done: {_report.Checks.Count(c => c.Pass)}/{_report.Checks.Count} checks, {_report.Phases.Count(p => p.Pass)}/{_report.Phases.Count} phases pass");
        return ok ? 0 : 2;
    }

    private static void Log(string s) => Console.WriteLine("[spike] " + s);

    private static string Rgb(Color c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static Rect Deflate(Rect r, double d) => new(r.X + d, r.Y + d, Math.Max(0, r.Width - (2 * d)), Math.Max(0, r.Height - (2 * d)));

    private async Task RunCoreAsync()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "mail");
        var shellJs = await File.ReadAllTextAsync(Path.Combine(assets, "shell.js"));
        _darkJs = await File.ReadAllTextAsync(Path.Combine(assets, "email_dark.js"));
        _shell = new ShellHost(_root.Body);
        var shellUp = new TaskCompletionSource();
        _shell.OnMessage = m =>
        {
            lock (_messages)
            {
                _messages.Add(m);
            }

            if (m == "shell")
            {
                shellUp.TrySetResult();
            }
        };

        var t0 = Clock.Now;
        await _shell.InitializeAsync(MailDocument.Shell(shellJs, Rgb(SpikeRoot.TileColor)), SpikeRoot.TileColor);
        _report.Notes.AddRange(_shell.SetupNotes.Select(n => "setup: " + n));
        var up = await Wait(shellUp.Task, 20_000);
        Check("shell-up (NavigateToString + postMessage 'shell')", up, $"{Clock.Ms(t0, Clock.Now):F0} ms after EnsureCoreWebView2Async started");
        if (!up)
        {
            return;
        }

        await ProbeApisAsync();
        _capture = CreateCapture();
        _report.Capture = _capture?.Name ?? "none";
        if (_capture is null)
        {
            return;
        }

        await SwitchPhaseAsync();
        await DarkPhaseAsync();
        await AirspaceFlyoutPhaseAsync();
        await AirspaceDialogPhaseAsync();
        await AirspaceCustomOverlayPhaseAsync();
#if !WINDOWS
        await LinuxStandinPhaseAsync();
#endif
    }

    private IFrameSource? CreateCapture()
    {
        try
        {
#if WINDOWS
            return new WgcCapture(HiddenWindow.Handle(_window));
#else
            return OperatingSystem.IsLinux() ? new X11Capture() : null;
#endif
        }
        catch (Exception e)
        {
            Check("capture available", false, e.GetType().Name + ": " + e.Message);
            return null;
        }
    }

    private void Check(string name, bool pass, string detail)
    {
        _report.Checks.Add(new CheckResult(name, pass, detail));
        Log($"{(pass ? "PASS" : "FAIL")} {name}: {detail}");
    }

    private bool SawMessage(string prefix)
    {
        lock (_messages)
        {
            return _messages.Exists(m => m.StartsWith(prefix, StringComparison.Ordinal));
        }
    }

    private static async Task<bool> Wait(Task t, int ms)
    {
        try
        {
            await t.WaitAsync(TimeSpan.FromMilliseconds(ms));
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Shows a page and, when it is ready, changes the header swatch and reveals it in the same dispatcher turn (the Rust ready/reveal contract).</summary>
    private async Task<double?> ShowAsync(string html, string bg, bool script, ShellHost.Transport transport, Color swatch, string header, int timeoutMs = 10_000)
    {
        var done = new TaskCompletionSource();
        long seq = 0;
        var t = Clock.Now;
        double ms = 0;
        _shell.OnReady = n =>
        {
            if (n != seq)
            {
                return;
            }

            _root.Swatch.Background = new SolidColorBrush(swatch);
            _root.HeaderText.Text = header;
            _shell.Reveal(n);
            ms = Clock.Ms(t, Clock.Now);
            done.TrySetResult();
        };
        seq = _shell.Show(html, bg, script, transport);
        return await Wait(done.Task, timeoutMs) ? ms : null;
    }

    private PhaseRecorder Recorder(string phase, IEnumerable<Region> regions, IEnumerable<ColorClass> classes, Func<FrameStats, string?>? save = null) =>
        new(regions, classes, SpikeRoot.Marker, SpikeRoot.MarkerSize)
        {
            SaveDir = _outDir,
            SaveWhen = save is null ? null : s => save(s) is { } k ? phase + "-" + k : null,
        };

    private async Task<IReadOnlyList<FrameStats>> CaptureAsync(PhaseRecorder rec, Func<Task> action, int settleMs = 400)
    {
        _capture!.Start(rec.Add);
        await Task.Delay(150);
        await action();
        await Task.Delay(settleMs);
        await _capture.StopAsync();
        if (rec.Scale > 0)
        {
            _report.Scale = rec.Scale;
        }

        return rec.Frames;
    }

    private void Phase(string name, bool pass, IReadOnlyList<FrameStats> frames, string verdict, Dictionary<string, double> metrics)
    {
        var located = frames.Where(f => f.Located).ToList();
        double gap = 0;
        for (var i = 1; i < frames.Count; i++)
        {
            gap = Math.Max(gap, Clock.Ms(frames[i - 1].Time, frames[i].Time));
        }

        var duration = frames.Count > 1 ? Clock.Ms(frames[0].Time, frames[^1].Time) : 0;
        metrics["located-frames"] = located.Count;
        _report.Phases.Add(new PhaseResult(name, pass && located.Count > 0, frames.Count, Math.Round(duration, 1), Math.Round(gap, 1), verdict, metrics));
        Log($"{(pass && located.Count > 0 ? "PASS" : "FAIL")} phase {name}: {frames.Count} frames ({located.Count} located), {verdict}");
    }
}
