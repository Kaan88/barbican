using System.Globalization;

namespace Barbican.App.Composition;

/// <summary>
/// The start decision of the App (docs/milestones/M1-plan.md §5 F1 step 3), made once, before the
/// window exists. UI-free so tests/Barbican.Automation.Tests covers it.
/// <list type="bullet">
/// <item>An AUTOMATION build is always hidden, whatever the arguments or the environment say.</item>
/// <item><c>--smoke REPORT.json</c> (any configuration, also Release) is hidden.</item>
/// <item>Hidden means <see cref="HiddenWindowMode.OffScreen"/> where the head can hide a window (the
/// WinUI head), else <see cref="HiddenWindowMode.PrivateDisplay"/> on Linux under the private Xvfb
/// that tools/linux-remote.sh announces with <c>BARBICAN_XVFB=1</c>. Anything else is an error, and
/// the App exits before it creates a window.</item>
/// </list>
/// </summary>
public sealed record HiddenWindowOptions(
    HiddenWindowMode Mode,
    string? SmokeReportPath,
    int AutomationPort,
    string Profile,
    string? Error)
{
    public const string SmokeArgument = "--smoke";
    public const int DefaultAutomationPort = 8792;

    /// <summary>Logical size of a hidden window (the screenshot size of the M1 visual review).</summary>
    public const int HiddenWidth = 1400;

    public const int HiddenHeight = 900;

    public bool IsSmoke => SmokeReportPath is not null;

    public bool IsHidden => Mode != HiddenWindowMode.Normal;

    /// <param name="args">Command-line arguments without the program path.</param>
    /// <param name="environment">Reads an environment variable (null when unset).</param>
    /// <param name="automationBuild">True when compiled with AUTOMATION.</param>
    /// <param name="isWindows">The OS the App runs on.</param>
    /// <param name="canHideWindow">The head implements the off-screen window (the WinUI head on Windows).</param>
    public static HiddenWindowOptions Decide(
        IReadOnlyList<string> args,
        Func<string, string?> environment,
        bool automationBuild,
        bool isWindows,
        bool canHideWindow)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        var (smoke, error) = Smoke(args);

        var port = DefaultAutomationPort;
        if (int.TryParse(environment("BARBICAN_AUTOMATION_PORT"), NumberStyles.None, CultureInfo.InvariantCulture, out var p) && p is > 1023 and < 65536)
        {
            port = p;
        }

        var hidden = automationBuild || smoke is not null;
        var profile = environment("BARBICAN_PROFILE") is { Length: > 0 } named
            ? named
            : automationBuild ? string.Create(CultureInfo.InvariantCulture, $"auto-{port}") : smoke is not null ? "smoke" : "default";

        var mode = HiddenWindowMode.Normal;
        if (hidden)
        {
            (mode, var hideError) = HiddenMode(environment, isWindows, canHideWindow);
            error ??= hideError;
        }

        return new HiddenWindowOptions(
            mode,
            string.IsNullOrEmpty(smoke) ? null : smoke,
            port,
            profile,
            error);
    }

    /// <summary>The report path after <c>--smoke</c>; an empty path (with an error) when it is missing.</summary>
    private static (string? Smoke, string? Error) Smoke(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], SmokeArgument, StringComparison.Ordinal))
            {
                return i + 1 < args.Count && args[i + 1].Length > 0 && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                    ? (args[i + 1], null)
                    : (string.Empty, "--smoke needs the path of the report file");
            }
        }

        return (null, null);
    }

    /// <summary>How a hidden run hides; never a visible window: where hiding is impossible, an error (the App exits).</summary>
    private static (HiddenWindowMode Mode, string? Error) HiddenMode(Func<string, string?> environment, bool isWindows, bool canHideWindow)
    {
        if (canHideWindow && isWindows)
        {
            return (HiddenWindowMode.OffScreen, null);
        }

        var xvfb = !isWindows
            && string.Equals(environment("BARBICAN_XVFB"), "1", StringComparison.Ordinal)
            && !string.IsNullOrEmpty(environment("DISPLAY"));
        if (xvfb)
        {
            return (HiddenWindowMode.PrivateDisplay, null);
        }

        return (HiddenWindowMode.OffScreen, isWindows
            ? "hidden mode is implemented by the WinUI head only; the desktop head never runs hidden on Windows"
            : "hidden mode on Linux runs only under a private Xvfb (tools/linux.ps1 xrun sets BARBICAN_XVFB=1)");
    }
}
