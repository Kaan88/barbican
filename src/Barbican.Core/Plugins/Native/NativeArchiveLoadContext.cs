using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Barbican.Core.Plugins.Packages.Inspection;
using Barbican.Plugins;

namespace Barbican.Core.Plugins.Native;

/// <summary>Resolves the archive's managed dependencies without staging executable files.</summary>
internal sealed class NativeArchiveLoadContext(InspectedPluginPackage package)
    : AssemblyLoadContext("native-package:" + package.Id, isCollectible: true)
{
    private static readonly Assembly Api = typeof(IBarbicanPlugin).Assembly;
    private static readonly string? ApiIdentity = Api.FullName;
    private static readonly string RuntimeDirectory = Path.TrimEndingDirectorySeparator(RuntimeEnvironment.GetRuntimeDirectory());
    private readonly Dictionary<string, ImmutableArray<byte>> _libraries = package.Resources
        .Where(file => file.Key.EndsWith(".dll", StringComparison.Ordinal))
        .ToDictionary(file => file.Key, file => file.Value, StringComparer.OrdinalIgnoreCase);

    [RequiresUnreferencedCode("Native add-ons run in the Windows JIT application.")]
    internal Assembly LoadMain(string entrypoint) => LoadLibrary(entrypoint, _libraries[entrypoint]);

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Only created in the Windows JIT application.")]
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, Api.GetName().Name, StringComparison.OrdinalIgnoreCase))
            return string.Equals(assemblyName.FullName, ApiIdentity, StringComparison.Ordinal) ? Api
                : throw new FileLoadException("The native add-on references an unsupported Barbican plug-in API.");

        var name = assemblyName.Name ?? string.Empty;
        if (_libraries.TryGetValue(name + ".dll", out var bytes))
            return LoadLibrary(name + ".dll", bytes);

        if (IsFrameworkName(name) && AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trusted &&
            trusted.Split(Path.PathSeparator).Any(path =>
                string.Equals(Path.GetDirectoryName(path), RuntimeDirectory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.OrdinalIgnoreCase)))
            return Default.LoadFromAssemblyName(assemblyName);

        throw new FileLoadException("A managed dependency was absent from the native add-on archive.");
    }

    private static bool IsFrameworkName(string name) => name is "System" or "mscorlib" or "netstandard" or "Microsoft.CSharp" ||
        name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Microsoft.Win32.", StringComparison.OrdinalIgnoreCase);

    [RequiresUnreferencedCode("Native add-ons run in the Windows JIT application.")]
    private Assembly LoadLibrary(string filename, ImmutableArray<byte> bytes)
    {
        using var content = new MemoryStream(bytes.ToArray(), writable: false);
        var assembly = LoadFromStream(content);
        if (!string.Equals(assembly.GetName().Name, Path.GetFileNameWithoutExtension(filename), StringComparison.OrdinalIgnoreCase))
            throw new FileLoadException("The native add-on library does not match its file name.");
        return assembly;
    }
}
