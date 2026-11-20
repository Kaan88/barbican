// Mirrors: Bulwark proxy.ts:365-480 and host-bridge.ts:356-370; native resource and navigation enforcement supplement CSP.
#pragma warning disable CA1822 // Policy is an injected instance; tier-specific rules can evolve.
using Barbican.Core.Plugins.Permissions;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Closed isolated-origin policy. Asset paths must come from the host's verified bootstrap manifest.</summary>
public sealed class PluginSandboxResourcePolicy
{
    internal const string HostOrigin = "https://plugin.invalid";
    internal const string ManagedChildOrigin = "https://managed-plugin.invalid";
    internal const string HostPath = "/host/index.html";
    internal const string ChildPath = "/sandbox/index.html";
    private readonly Dictionary<string, string> _assets;
    private readonly PluginExecutionTier _tier;

    public PluginSandboxResourcePolicy(IEnumerable<(string Path, string Purpose)>? bootstrapAssets = null)
        : this(PluginExecutionTier.Untrusted, bootstrapAssets) { }

    internal PluginSandboxResourcePolicy(PluginExecutionTier tier, IEnumerable<(string Path, string Purpose)>? bootstrapAssets = null)
    {
        _tier = tier;
        ChildOrigin = ChildOriginFor(tier);
        _assets = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (path, purpose) in bootstrapAssets ?? [])
        {
            if (!SafeAsset(path) || purpose is not ("script" or "style" or "font" or "image") || !_assets.TryAdd(path, purpose))
                throw new ArgumentException("Invalid or duplicate bootstrap asset declaration.", nameof(bootstrapAssets));
        }
    }
    public string SandboxFlags => FlagsFor(_tier);
    /// <summary>The tier is selected by the host only after verifying its authorization.</summary>
    public string FlagsFor(PluginExecutionTier hostSelectedTier) => hostSelectedTier switch
    {
        PluginExecutionTier.Untrusted => "allow-scripts allow-forms",
        PluginExecutionTier.Privileged => "allow-scripts allow-same-origin",
        _ => throw new ArgumentOutOfRangeException(nameof(hostSelectedTier)),
    };
    internal const string ChildContentSecurityPolicy = "default-src 'none'; script-src 'self' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'none'; webrtc 'block'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; worker-src blob:; frame-ancestors https://plugin.invalid";
    public string ContentSecurityPolicy => ChildContentSecurityPolicy;
    internal string ChildOrigin { get; }

    internal static string ChildOriginFor(PluginExecutionTier tier) => tier switch
    {
        PluginExecutionTier.Untrusted => HostOrigin,
        PluginExecutionTier.Privileged => ManagedChildOrigin,
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    internal static bool IsParentScript(string path) => path is "/assets/plugin-sandbox-host.js" or
        "/assets/plugin-sandbox-parent.js" or "/assets/plugin-value-codec.js";

    /// <summary>Only known documents and exact verified bootstrap assets may be served. Network requests remain denied.</summary>
    public bool Allows(Uri uri, string purpose)
    {
        ArgumentNullException.ThrowIfNull(uri);
        // Check the original spelling before Uri canonicalizes dot segments, ports or escapes.
        if (!uri.IsAbsoluteUri ||
            uri.OriginalString.Contains('%', StringComparison.Ordinal) || uri.OriginalString.Contains('\\', StringComparison.Ordinal) ||
            uri.OriginalString.Contains('#', StringComparison.Ordinal) || uri.OriginalString.Contains("/../", StringComparison.Ordinal) ||
            uri.OriginalString.Contains("/./", StringComparison.Ordinal)) return false;
        var path = uri.AbsolutePath;
        var query = uri.Query;
        return purpose switch
        {
            "top-navigation" => path is HostPath && query.Length == 0 && HasOrigin(uri, HostOrigin),
            "frame-navigation" => path is ChildPath && (query is "" or "?frame=slot") && HasOrigin(uri, ChildOrigin),
            _ => query.Length == 0 && HasOrigin(uri, IsParentScript(path) ? HostOrigin : ChildOrigin) &&
                _assets.TryGetValue(path, out var assetPurpose) && string.Equals(assetPurpose, purpose, StringComparison.Ordinal),
        };
    }

    private static bool HasOrigin(Uri uri, string origin) =>
        string.Equals(uri.OriginalString, origin + uri.AbsolutePath + uri.Query, StringComparison.Ordinal);

    private static bool SafeAsset(string? path) => path is { Length: > 8 and <= 240 } && path.StartsWith("/assets/", StringComparison.Ordinal) &&
        path.AsSpan(8).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".AsSpan()) < 0 &&
        !path.Contains("..", StringComparison.Ordinal);
}
