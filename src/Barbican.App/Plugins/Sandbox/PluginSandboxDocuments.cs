// Mirrors: Bulwark lib/plugin-sandbox/sandbox-document.ts:53-66 and proxy.ts:334-386,459-480.
using Barbican.Core.Plugins.Runtime;

namespace Barbican.App.Plugins.Sandbox;

/// <summary>Fixed documents for the trusted native bridge and its opaque child.</summary>
internal static class PluginSandboxDocuments
{
    private const string CommonHeaders = "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nConnection-Allowlist: (response-origin); redirects=block; webrtc=block\r\n";
    private const string HostPolicy = "default-src 'none'; script-src 'self'; style-src 'unsafe-inline'; frame-src 'self'; connect-src 'none'; webrtc 'block'; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    internal static (string Html, string Headers) Host(PluginFrameKind kind)
    {
        var frameKind = kind switch
        {
            PluginFrameKind.Background => "background",
            PluginFrameKind.Slot => "slot",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return ($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex, nofollow\"><title>Plugin host</title><style>html,body{{margin:0;width:100%;height:100%}}iframe{{display:block;border:0}}</style><script type=\"module\" src=\"/assets/plugin-sandbox-host.js\"></script></head><body data-frame-kind=\"{frameKind}\"></body></html>",
            CommonHeaders + "Content-Security-Policy: " + HostPolicy + "\r\n");
    }

    internal static (string Html, string Headers) Child() =>
        ($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"robots\" content=\"noindex, nofollow\"><title>Plugin sandbox</title><link rel=\"stylesheet\" href=\"/assets/{PluginSandboxBootstrapAssets.Style}\"><script type=\"module\" src=\"/assets/{PluginSandboxBootstrapAssets.EntryScript}\"></script></head><body style=\"margin:0;padding:0;background:transparent\"><div id=\"plugin-sandbox-root\"></div></body></html>",
            CommonHeaders + "Content-Security-Policy: " + PluginSandboxResourcePolicy.ChildContentSecurityPolicy + "\r\n");
}
