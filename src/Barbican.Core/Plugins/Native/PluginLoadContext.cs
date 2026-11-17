// Mirrors: Rust plugins.rs:73 load (one library per plugin, kept loaded while the app runs), replaced by a collectible AssemblyLoadContext per plugin folder
// (M2 plan 6.5: only Barbican.Plugins.Abstractions is shared with the app); PORT-PLAN M6b (two plugin systems over one catalogue). Owner unified (M2 wave 2).

using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;

namespace Barbican.Core.Plugins.Native;

/// <summary>
/// The load context of one plugin. The plugin API (Barbican.Plugins.Abstractions) comes from the app so the interface types match; a reference
/// by name to any other Barbican assembly is refused, which stops a plugin from linking against the app's internals by accident; the plugin's own
/// dependencies load from its folder; the framework comes from the default context. Collectible, so a plugin can be unloaded.
/// This is not a security boundary: a plugin runs in the app's process with full trust and can reach everything through reflection over the
/// default context, the file system or the network. Only plugins the app ships are loaded until the M6b permission UI (Fable review 3 R3-04).
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private const string SharedApi = "Barbican.Plugins.Abstractions";

    private readonly string _folder;
    private readonly AssemblyDependencyResolver _resolver;

    public PluginLoadContext(string pluginPath)
        : base($"plugin:{Path.GetFileName(Path.GetDirectoryName(pluginPath))}", isCollectible: true)
    {
        _folder = Path.GetDirectoryName(pluginPath)!;
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Plugins load only in the JIT app (NativePluginHost.Load checks RuntimeFeature.IsDynamicCodeSupported).")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        var name = assemblyName.Name ?? string.Empty;
        if (string.Equals(name, SharedApi, StringComparison.Ordinal))
        {
            // the app's copy: the IBarbicanPlugin a plugin implements must be the type the host knows
            return null;
        }

        if (name.StartsWith("Barbican.", StringComparison.Ordinal) && !File.Exists(Path.Combine(_folder, name + ".dll")))
        {
            throw new FileLoadException($"A plugin may use only {SharedApi} of the app, not {name}.");
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        if (path is null)
        {
            var local = Path.Combine(_folder, name + ".dll");
            path = File.Exists(local) ? local : null;
        }

        // null: the framework (System.*) from the default context
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
