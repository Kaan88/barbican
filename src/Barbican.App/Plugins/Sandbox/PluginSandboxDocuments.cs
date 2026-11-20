// Mirrors: Bulwark lib/plugin-sandbox/sandbox-document.ts:53-66 and proxy.ts:334-386,459-480.
using Barbican.Core.Plugins.Permissions;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Fixed documents for the trusted native bridge and its isolated child.</summary>
internal static class PluginSandboxDocuments
{
    private const string CommonHeaders = "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n";
    private const string HostPolicy = "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; connect-src 'none'; webrtc 'block'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'; frame-src ";

    internal static (string Html, string Headers) Host(PluginFrameKind kind, PluginExecutionTier tier = PluginExecutionTier.Untrusted)
    {
        var frameKind = kind switch
        {
            PluginFrameKind.Background => "background",
            PluginFrameKind.Slot => "slot",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var childOrigin = PluginSandboxResourcePolicy.ChildOriginFor(tier);
        var privileged = tier == PluginExecutionTier.Privileged;
        var frameTier = privileged ? "privileged" : "untrusted";
        var frameSource = privileged ? childOrigin + PluginSandboxResourcePolicy.ChildPath : "'self'";
        return ($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex, nofollow\"><title>Plugin host</title><style>html,body{{margin:0;width:100%;height:100%}}iframe{{display:block;border:0}}</style><script type=\"module\" src=\"/assets/plugin-sandbox-host.js\"></script></head><body data-frame-kind=\"{frameKind}\" data-frame-tier=\"{frameTier}\"></body></html>",
            Headers(HostPolicy + frameSource, reportLocally: false, privileged, privileged ? childOrigin : null));
    }

    internal static (string Html, string Headers) Child(PluginExecutionTier tier = PluginExecutionTier.Untrusted)
    {
        var parent = tier switch
        {
            PluginExecutionTier.Untrusted => "",
            PluginExecutionTier.Privileged => $"<meta name=\"barbican-parent-origin\" content=\"{PluginSandboxResourcePolicy.HostOrigin}\">",
            _ => throw new ArgumentOutOfRangeException(nameof(tier)),
        };
        return ($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex, nofollow\">{parent}<meta name=\"barbican-runtime\" content=\"/assets/{PluginSandboxBootstrapAssets.EntryScript}\"><title>Plugin sandbox</title><link rel=\"stylesheet\" href=\"/assets/{PluginSandboxBootstrapAssets.Style}\"><script type=\"module\" src=\"/assets/plugin-sandbox-network.js\"></script></head><body style=\"margin:0;padding:0;background:transparent\"><div id=\"plugin-sandbox-root\"></div></body></html>",
            Headers(PluginSandboxResourcePolicy.ChildContentSecurityPolicy, reportLocally: true, tier == PluginExecutionTier.Privileged));
    }

    // Chromium queues WebRTC policy reports only when a group is named. The
    // child names a group without registering a reporting destination.
    private static string Headers(string csp, bool reportLocally, bool privileged, string? frameOrigin = null) =>
        CommonHeaders + (privileged ? "Origin-Agent-Cluster: ?1\r\n" : "") +
        "Connection-Allowlist: (response-origin" + (frameOrigin is null ? "" : $" \"{frameOrigin}\"") +
        "); redirects=block; webrtc=block" + (reportLocally ? "; report-to=barbican-local-probe" : "") +
        "\r\nContent-Security-Policy: " + csp + "\r\n";
}
