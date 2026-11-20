// Synthetic sentinel: if discovery ever loads this test-only assembly, a temp marker exposes the bug.
using System.Runtime.CompilerServices;
using Barbican.Plugins;

namespace Barbican.NativeSentinel;

/// <summary>Never execute during inspection or before explicit native full-trust approval.</summary>
public sealed class SentinelPlugin : IBarbicanPlugin
{
#pragma warning disable CA2255 // A module initializer is the behavior this synthetic security fixture detects.
    [ModuleInitializer]
    internal static void ModuleStart() => Mark("module");
#pragma warning restore CA2255
    public SentinelPlugin() => Mark("constructor");
    public PluginManifest Manifest { get { Mark("manifest"); return new(1, "sentinel-fixture", "Sentinel", "1.0.0", "Synthetic", ["email.details"]); } }
    public Task InitializeAsync(PluginContext context, CancellationToken ct) => Task.CompletedTask;
    public Task<IReadOnlyList<EmailDetailRow>> OnEmailDetailsAsync(EmailDetailsView email, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<EmailDetailRow>>([new("Native fixture", "Reader details reached the approved add-on.", "neutral")]);
    private static void Mark(string value)
    {
        var directory = Path.Combine(Path.GetTempPath(), "barbican-native-sentinel-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Directory.Exists(directory)) File.AppendAllText(Path.Combine(directory, "activity"), value);
    }
}
