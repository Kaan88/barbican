using Microsoft.Extensions.Logging;
using Uno.UI.Hosting;

namespace Barbican.Spike.BodyShell;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Uno's own diagnostics (native WebView start-up, not-implemented calls) to stderr.
        var level = Environment.GetEnvironmentVariable("BARBICAN_SPIKE_UNO_LOG") == "debug" ? LogLevel.Debug : LogLevel.Information;
        using var factory = LoggerFactory.Create(b => b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace).SetMinimumLevel(level));
        Uno.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
        Uno.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();

        UnoPlatformHostBuilder.Create()
            .App(() => new SpikeApp())
            .UseX11()
            .UseWin32()
            .Build()
            .Run();
    }
}
