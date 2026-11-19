// Mirrors: Bulwark proxy.ts:365-480 and host-bridge.ts:356-370; native resource and navigation enforcement supplement CSP.
#pragma warning disable CA1822 // Policy is an injected instance; tier-specific rules can evolve.
using Barbican.Core.Plugins.Permissions;
namespace Barbican.App.Plugins.Sandbox;
/// <summary>Closed isolated-origin policy. Asset paths must come from the host's verified bootstrap manifest.</summary>
public sealed class PluginSandboxResourcePolicy
{
    private const string Host = "plugin.invalid";
    private readonly HashSet<string> _assets;
    public PluginSandboxResourcePolicy(IEnumerable<string>? bootstrapAssets = null)
    {
        _assets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in bootstrapAssets ?? [])
        {
            if (!SafeAsset(path) || !_assets.Add(path)) throw new ArgumentException("Invalid or duplicate bootstrap asset path.", nameof(bootstrapAssets));
        }
    }
    public string SandboxFlags => FlagsFor(PluginExecutionTier.Untrusted);
    /// <summary>The tier is selected by the host only after verifying its authorization.</summary>
    public string FlagsFor(PluginExecutionTier hostSelectedTier) => hostSelectedTier switch
    {
        PluginExecutionTier.Untrusted => "allow-scripts allow-forms",
        PluginExecutionTier.Privileged => "allow-scripts allow-forms allow-same-origin",
        _ => throw new ArgumentOutOfRangeException(nameof(hostSelectedTier)),
    };
    public string ContentSecurityPolicy => "default-src 'none'; script-src 'self' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; font-src 'self' data:; connect-src 'none'; frame-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'; worker-src blob:";

    /// <summary>Only known documents and exact verified bootstrap assets may be served. Network requests remain denied.</summary>
    public bool Allows(Uri uri, string purpose)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(uri.IdnHost, Host, StringComparison.OrdinalIgnoreCase) || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.OriginalString.Contains('%', StringComparison.Ordinal) || uri.OriginalString.Contains('\\', StringComparison.Ordinal) ||
            uri.OriginalString.Contains("/../", StringComparison.Ordinal) || uri.OriginalString.Contains("/./", StringComparison.Ordinal)) return false;
        var path = uri.AbsolutePath;
        if (string.Equals(purpose, "top-navigation", StringComparison.Ordinal)) return string.Equals(path, "/host/index.html", StringComparison.Ordinal) && uri.Query.Length == 0;
        if (string.Equals(purpose, "frame-navigation", StringComparison.Ordinal)) return string.Equals(path, "/sandbox/index.html", StringComparison.Ordinal) && (uri.Query.Length == 0 || string.Equals(uri.Query, "?frame=slot", StringComparison.Ordinal));
        return uri.Query.Length == 0 && _assets.Contains(path) && purpose is "script" or "style" or "font" or "image";
    }
    private static bool SafeAsset(string? path) => path is { Length: > 8 and <= 240 } && path.StartsWith("/assets/", StringComparison.Ordinal) &&
        path.AsSpan(8).IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".AsSpan()) < 0 &&
        !path.Contains("..", StringComparison.Ordinal);
}
