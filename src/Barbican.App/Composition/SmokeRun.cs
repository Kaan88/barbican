using System.Globalization;
using Barbican.Core.Mail.Body;

namespace Barbican.App.Composition;

/// <summary>
/// <c>Barbican --smoke REPORT.json</c> (docs/milestones/M1-plan.md §5 F1 step 3; also in Release): the
/// window is hidden from its first frame (<see cref="HiddenWindowOptions"/>), the run waits for the
/// first rendered frame and for the body shell's <c>shell</c> message, writes the cold-start timings
/// and exits with 0 (2: bad arguments, 3: timeout, 4: a failure on the way).
/// <para>Until the body stream's warm shell host exists, the smoke run starts its own small WebView2
/// with the real shell page (Core's <see cref="ShellDocument"/> and Assets/mail/shell.js) next to the
/// shell page, and validates the reply through <see cref="ShellMessageGate"/>.</para>
/// </summary>
internal sealed class SmokeRun
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private readonly Window _window;
    private readonly HiddenWindowOptions _options;
    private readonly double _launchedMs;
    private readonly ShellMessageGate _gate = new();
    private readonly WebView2 _probe = new() { Width = 64, Height = 64, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private double? _firstFrameMs;
    private bool _done;

    public SmokeRun(Window window, HiddenWindowOptions options, double launchedMs)
    {
        _window = window;
        _options = options;
        _launchedMs = launchedMs;
    }

    /// <summary>The window content for a smoke run: the shell page with the probe shell next to it.</summary>
    public UIElement Wrap(UIElement shell)
    {
        var grid = new Grid();
        grid.Children.Add(shell);
        grid.Children.Add(_probe);
        return grid;
    }

    public void Start()
    {
        CompositionTarget.Rendering += OnRendering;
        _probe.WebMessageReceived += (_, e) =>
        {
            string? raw;
            try
            {
                raw = e.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                raw = null;
            }

            if (_gate.TryAccept(raw, out var message) && message.Kind == ShellMessageKind.Shell)
            {
                Finish(null, 0);
            }
        };
        _probe.Loaded += (_, _) => _ = LoadShellAsync();
        _ = WatchdogAsync();
    }

    private async Task LoadShellAsync()
    {
        try
        {
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "mail", "shell.js"));
            await MailWebViewStartup.EnsureAsync(_probe, _options.IsHidden);
            _probe.NavigateToString(ShellDocument.Build(script, "#141414"));
        }
#pragma warning disable CA1031 // a smoke run reports every failure in its report instead of crashing
        catch (Exception e)
#pragma warning restore CA1031
        {
            Finish("shell failed: " + e.GetType().Name + ": " + e.Message, 4);
        }
    }

    private void OnRendering(object? sender, object e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _firstFrameMs ??= ProcessClock.SinceStartMs();
    }

    private async Task WatchdogAsync()
    {
        await Task.Delay(Timeout);
        Finish(string.Create(CultureInfo.InvariantCulture, $"no shell message after {Timeout.TotalSeconds} s (first frame: {(_firstFrameMs is null ? "no" : "yes")})"), 3);
    }

    private void Finish(string? error, int exitCode)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        var shellMs = error is null ? ProcessClock.SinceStartMs() : (double?)null;
        var report = new SmokeReport(
            error is null && _firstFrameMs is not null,
            error ?? (_firstFrameMs is null ? "no frame was rendered" : null),
            Configuration(),
            OperatingSystem.IsWindows() ? "win" : "desktop",
            _launchedMs,
            _firstFrameMs,
            shellMs,
            error is null && _firstFrameMs is null ? 4 : exitCode);
        Write(_options.SmokeReportPath!, report);
        _window.DispatcherQueue.TryEnqueue(() => Environment.Exit(report.ExitCode));
    }

    /// <summary>Writes the report; used by the App too when the arguments are already wrong.</summary>
    public static void Write(string path, SmokeReport report)
    {
        try
        {
            var full = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, report.ToJson());
        }
        catch (IOException e)
        {
            Console.Error.WriteLine("[smoke] cannot write the report: " + e.Message);
        }
        catch (UnauthorizedAccessException e)
        {
            Console.Error.WriteLine("[smoke] cannot write the report: " + e.Message);
        }

        Console.WriteLine("[smoke] " + report.ToJson().ReplaceLineEndings(" "));
    }

    public static string Configuration() =>
        typeof(SmokeRun).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "BarbicanConfiguration", StringComparison.Ordinal))?.Value ?? "unknown";
}
