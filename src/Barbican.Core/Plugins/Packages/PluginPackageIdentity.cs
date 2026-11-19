// Mirrors: Bulwark lib/plugin-types.ts:129-187 and lib/plugin-sandbox/tier.ts:30-58.
// M6b-contracts: consent is bound to the complete validated package hash, not only its displayed id/version.
namespace Barbican.Core.Plugins.Packages;

/// <summary>Content identity of a complete package; this value alone does not prove validation or signature trust.</summary>
public sealed record PluginPackageIdentity
{
    public PluginPackageIdentity(string id, string version, string sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(sha256);
        if (id.Length > 256 || id.Any(char.IsControl)) throw new ArgumentException("Invalid plugin id.", nameof(id));
        if (version.Length > 256 || version.Any(char.IsControl)) throw new ArgumentException("Invalid plugin version.", nameof(version));
        if (sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("A SHA-256 package hash is required.", nameof(sha256));
        Id = id;
        Version = version;
        Sha256 = sha256.ToLowerInvariant();
    }

    public string Id { get; }
    public string Version { get; }
    public string Sha256 { get; }
}
