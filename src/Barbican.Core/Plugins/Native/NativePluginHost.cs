// Mirrors: Rust plugins.rs:load (a plugin per folder, manifest check, hook list) replaced by an AssemblyLoadContext per plugin folder (docs/legacy/PLUGINS.md is the
// old C ABI, dropped). Stub from stage F of M2; owner unified. The bundled plugin is built to <output>/plugins/spam-score/ (M2 plan 3.2).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>Loads native .NET plugins from a plugins folder (one subfolder per plugin).</summary>
public sealed class NativePluginHost : INativePluginHost
{
    /// <inheritdoc />
    public IReadOnlyList<PluginInfo> Plugins => throw new PortPendingException("plugins.rs load: NativePluginHost.Plugins");

    /// <summary>Loads every plugin under <paramref name="pluginsRoot"/>; a plugin that fails to load is skipped and logged.</summary>
    public static Result<NativePluginHost> LoadDirectory(string pluginsRoot)
    {
        throw new PortPendingException("plugins.rs load: NativePluginHost.LoadDirectory");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailDetailRow>> EmailDetailsAsync(JsonNode email, CancellationToken ct)
    {
        throw new PortPendingException("plugins.rs email_details");
    }
}
