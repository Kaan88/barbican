using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Runtime;

namespace Barbican.Automation.Tests.Plugins;

public sealed class PluginSandboxDocumentsTests
{
    [Theory]
    [InlineData(PluginFrameKind.Background, "background")]
    [InlineData(PluginFrameKind.Slot, "slot")]
    public void Host_contains_only_host_selected_kind_and_external_module(PluginFrameKind kind, string expected)
    {
        var (html, headers) = PluginSandboxDocuments.Host(kind);
        Assert.Contains($"data-frame-kind=\"{expected}\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/assets/plugin-sandbox-host.js\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", headers, StringComparison.Ordinal);
        Assert.Contains("frame-src 'self'", headers, StringComparison.Ordinal);
        Assert.Contains("connect-src 'none'", headers, StringComparison.Ordinal);
        Assert.Contains("webrtc 'block'", headers, StringComparison.Ordinal);
        Assert.Contains("Connection-Allowlist: (response-origin); redirects=block; webrtc=block", headers, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => PluginSandboxDocuments.Host((PluginFrameKind)999));
    }

    [Fact]
    public void Child_contains_pinned_entry_and_styles_and_restrictive_headers()
    {
        var (html, headers) = PluginSandboxDocuments.Child();
        Assert.Contains($"name=\"barbican-runtime\" content=\"/assets/{PluginSandboxBootstrapAssets.EntryScript}\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/assets/plugin-sandbox-network.js\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"src=\"/assets/{PluginSandboxBootstrapAssets.EntryScript}\"", html, StringComparison.Ordinal);
        Assert.Contains("/assets/" + PluginSandboxBootstrapAssets.Style, html, StringComparison.Ordinal);
        Assert.Contains("id=\"plugin-sandbox-root\"", html, StringComparison.Ordinal);
        Assert.Contains("script-src 'self' 'unsafe-eval'", headers, StringComparison.Ordinal);
        Assert.Contains("connect-src 'none'", headers, StringComparison.Ordinal);
        Assert.Contains("webrtc 'block'", headers, StringComparison.Ordinal);
        Assert.Contains("Connection-Allowlist: (response-origin); redirects=block; webrtc=block", headers, StringComparison.Ordinal);
        Assert.Contains("frame-src 'none'", headers, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", headers, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors https://plugin.invalid", headers, StringComparison.Ordinal);
        Assert.Contains("Cache-Control: no-store", headers, StringComparison.Ordinal);
        Assert.Contains("X-Content-Type-Options: nosniff", headers, StringComparison.Ordinal);
    }

    [Fact]
    public void Child_can_observe_locally_without_a_report_destination()
    {
        var (_, hostHeaders) = PluginSandboxDocuments.Host(PluginFrameKind.Background);
        var (_, childHeaders) = PluginSandboxDocuments.Child();

        Assert.DoesNotContain("report-to", hostHeaders, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("webrtc=block; report-to=barbican-local-probe\r\n", childHeaders, StringComparison.Ordinal);
        Assert.DoesNotContain("Reporting-Endpoints", childHeaders, StringComparison.OrdinalIgnoreCase);
    }
}
