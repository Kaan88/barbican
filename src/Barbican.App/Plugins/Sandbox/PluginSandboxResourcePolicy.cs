// Mirrors: Bulwark proxy.ts:365-480 and host-bridge.ts:356-370; native resource and navigation enforcement supplement CSP.
#pragma warning disable CA1822 // Policy is an injected instance; tier-specific rules can evolve.
using Barbican.Core.Plugins.Permissions;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Closed isolated-origin policy. Asset paths must come from the host's verified bootstrap manifest.</summary>
public sealed class PluginSandboxResourcePolicy
{
    private readonly Dictionary<string, string> _assets;
    public PluginSandboxResourcePolicy(IEnumerable<(string Path, string Purpose)>? bootstrapAssets = null)
    {
        _assets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, purpose) in bootstrapAssets ?? [])
        {
            if (!SafeAsset(path) || purpose is not ("script" or "style" or "font" or "image") || !_assets.TryAdd(path, purpose))
                throw new ArgumentException("Invalid or duplicate bootstrap asset declaration.", nameof(bootstrapAssets));
        }
    }
    public string SandboxFlags => FlagsFor(PluginExecutionTier.Untrusted);
    /// <summary>The tier is selected by the host only after verifying its authorization.</summary>
    public string FlagsFor(PluginExecutionTier hostSelectedTier) => hostSelectedTier switch
    {
        PluginExecutionTier.Untrusted => "allow-scripts allow-forms",
        PluginExecutionTier.Privileged => "allow-scripts allow-same-origin",
        _ => throw new ArgumentOutOfRangeException(nameof(hostSelectedTier)),
    };
    public string ContentSecurityPolicy => "default-src 'none'; script-src 'self' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; worker-src blob:";

    /// <summary>Only known documents and exact verified bootstrap assets may be served. Network requests remain denied.</summary>
    public bool Allows(Uri uri, string purpose)
    {
        ArgumentNullException.ThrowIfNull(uri);
        // Check the original spelling before Uri canonicalizes dot segments, ports or escapes.
        // Only the exact synthetic origin is routed by this policy.
        if (!uri.IsAbsoluteUri || !uri.OriginalString.StartsWith("https://plugin.invalid/", StringComparison.Ordinal) ||
            uri.OriginalString.Contains('%', StringComparison.Ordinal) || uri.OriginalString.Contains('\\', StringComparison.Ordinal) ||
            uri.OriginalString.Contains('#', StringComparison.Ordinal) || uri.OriginalString.Contains("/../", StringComparison.Ordinal) ||
            uri.OriginalString.Contains("/./", StringComparison.Ordinal)) return false;
        var path = uri.AbsolutePath;
        if (!string.Equals(uri.OriginalString, "https://plugin.invalid" + path + uri.Query, StringComparison.Ordinal)) return false;
        if (string.Equals(purpose, "top-navigation", StringComparison.Ordinal)) return string.Equals(path, "/host/index.html", StringComparison.Ordinal) && uri.Query.Length == 0;
        // Only the untrusted frame route is supported until a host-selected privileged route is wired.
        if (string.Equals(purpose, "frame-navigation", StringComparison.Ordinal)) return string.Equals(path, "/sandbox/index.html", StringComparison.Ordinal) && (uri.Query.Length == 0 || string.Equals(uri.Query, "?frame=slot", StringComparison.Ordinal));
        return uri.Query.Length == 0 && _assets.TryGetValue(path, out var assetPurpose) && string.Equals(assetPurpose, purpose, StringComparison.Ordinal);
    }
    private static bool SafeAsset(string? path) => path is { Length: > 8 and <= 240 } && path.StartsWith("/assets/", StringComparison.Ordinal) &&
        path.AsSpan(8).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".AsSpan()) < 0 &&
        !path.Contains("..", StringComparison.Ordinal);
}
