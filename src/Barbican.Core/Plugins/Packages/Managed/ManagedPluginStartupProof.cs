namespace Barbican.Core.Plugins.Packages.Managed;

/// <summary>Host-only access to the approved entrypoint while its installed generation and provider session remain current.</summary>
public sealed class ManagedPluginStartupProof
{
    private readonly string _code;
    private readonly Func<Func<bool>, bool> _whileCurrent;

    internal ManagedPluginStartupProof(PluginPackageIdentity package, long installedGeneration,
        string code, Func<Func<bool>, bool> whileCurrent)
    {
        Package = package;
        InstalledGeneration = installedGeneration;
        _code = code;
        _whileCurrent = whileCurrent;
    }

    public PluginPackageIdentity Package { get; }
    public long InstalledGeneration { get; }

    internal bool TryRunWhileCurrent(Func<bool> issue) => _whileCurrent(issue);

    /// <summary>Hands the verified entrypoint to a synchronous host operation while its authority is held.</summary>
    public bool TryReadEntrypoint(Func<string, bool> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return _whileCurrent(() => read(_code));
    }
}
