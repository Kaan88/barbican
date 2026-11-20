// Source-derived policy tests: Bulwark proxy.ts:365-480, host-bridge.ts:356-370; M6b security invariants.
using Barbican.App.Plugins.Sandbox;
using Barbican.Core.Plugins.Permissions;
namespace Barbican.Automation.Tests.Plugins;
public sealed class PluginSandboxPolicyRedTests
{
    [Fact]
    public void Untrusted_frame_retains_form_events_but_cannot_submit_or_gain_same_origin()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.Equal("allow-scripts allow-forms", policy.SandboxFlags);
        Assert.Contains("form-action 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }
    [Fact]
    public void Verified_privileged_tier_has_same_origin_while_untrusted_tier_does_not()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.Equal("allow-scripts allow-same-origin", policy.FlagsFor(PluginExecutionTier.Privileged));
        Assert.Equal("allow-scripts allow-forms", policy.FlagsFor(PluginExecutionTier.Untrusted));
        Assert.Contains("form-action 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }
    [Fact]
    public void Native_full_trust_is_not_a_javascript_sandbox_tier()
    {
        var policy = new PluginSandboxResourcePolicy();
        try { _ = policy.FlagsFor(PluginExecutionTier.NativeFullTrust); }
        catch (ArgumentOutOfRangeException) { return; }
        Assert.Fail("Native full trust must not select a JavaScript sandbox.");
    }
    [Fact]
    public void Arbitrary_network_filesystem_and_route_escape_are_denied()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.False(policy.Allows(new Uri("https://example.org/path"), "fetch"));
        Assert.False(policy.Allows(new Uri("https://127.0.0.1/private"), "fetch"));
        Assert.False(policy.Allows(new Uri("file:///private.txt"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/mail/credentials"), "fetch"));
        Assert.False(policy.Allows(new Uri("https://example.org/redirect"), "navigation"));
    }
    [Fact]
    public void Only_exact_host_documents_and_verified_bootstrap_assets_are_allowed()
    {
        var policy = new PluginSandboxResourcePolicy([("/assets/runtime-a1.js", "script"), ("/assets/kit-a1.css", "style")]);
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/host/index.html"), "top-navigation"));
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/sandbox/index.html?frame=slot"), "frame-navigation"));
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/sandbox/index.html"), "frame-navigation"));
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/assets/kit-a1.css"), "style"));
        Assert.True(policy.Allows(new Uri("https://plugin.invalid/assets/runtime-a1.js"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/kit-a1.css"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/runtime-a1.js"), "style"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/privileged/index.html"), "frame-navigation"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/runtime-a1.js"), "fetch"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/not-reviewed.js"), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/sandbox/index.html"), "top-navigation"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/host/index.html?extra=1"), "top-navigation"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/%72untime-a1.js"), "script"));
    }

    [Fact]
    public void Privileged_parent_and_child_are_served_different_assets_on_different_origins()
    {
        const string host = "https://plugin.invalid";
        const string child = "https://managed-plugin.invalid";
        const string parentScript = "/assets/plugin-sandbox-parent.js";
        const string runtime = "/assets/runtime-a1.js";
        var policy = new PluginSandboxResourcePolicy(PluginExecutionTier.Privileged, [(parentScript, "script"), (runtime, "script")]);

        Assert.True(policy.Allows(new Uri(host + parentScript), "script"));
        Assert.True(policy.Allows(new Uri(child + runtime), "script"));
        Assert.False(policy.Allows(new Uri(child + parentScript), "script"));
        Assert.False(policy.Allows(new Uri(host + runtime), "script"));
        Assert.False(policy.Allows(new Uri(child + "/host/index.html"), "top-navigation"));
        Assert.False(policy.Allows(new Uri(child + runtime), "fetch"));
        Assert.False(new PluginSandboxResourcePolicy().Allows(new Uri(child + "/sandbox/index.html"), "frame-navigation"));
    }

    [Theory]
    [InlineData("https://plugin.invalid:443/host/index.html", "top-navigation")]
    [InlineData("https://plugin.invalid/host/%69ndex.html", "top-navigation")]
    [InlineData("https://plugin.invalid/host/index.html ", "top-navigation")]
    [InlineData("https://plugin.invalid.evil/host/index.html", "top-navigation")]
    [InlineData("https://plugin.invalid:444/host/index.html", "top-navigation")]
    [InlineData("https://plugin.invalid/host/../host/index.html", "top-navigation")]
    [InlineData("https://plugin.invalid/host/./index.html", "top-navigation")]
    [InlineData("https://plugin.invalid/host/index.html#", "top-navigation")]
    [InlineData("https://plugin.invalid/host/index.html?", "top-navigation")]
    [InlineData("https://plugin.invalid/sandbox/index.html?frame=background", "frame-navigation")]
    [InlineData("https://plugin.invalid/sandbox/index.html?frame=slot&x=1", "frame-navigation")]
    [InlineData("https://plugin.invalid/assets/runtime-a1.js?x=1", "script")]
    [InlineData("https://plugin.invalid/assets/runtime-a1.js", "navigation")]
    [InlineData("https://plugin.invalid/assets/runtime-a1.js", "fetch")]
    [InlineData("https://plugin.invalid/host/index.html", "frame-navigation")]
    public void Alternate_urls_queries_and_purposes_are_denied(string url, string purpose)
    {
        var policy = new PluginSandboxResourcePolicy([("/assets/runtime-a1.js", "script")]);
        Assert.False(policy.Allows(new Uri(url), purpose));
    }

    [Fact]
    public void Bootstrap_manifest_is_closed_and_rejects_unsafe_or_duplicate_paths()
    {
        Assert.Throws<ArgumentException>(() => new PluginSandboxResourcePolicy([("/assets/../secret", "script")]));
        Assert.Throws<ArgumentException>(() => new PluginSandboxResourcePolicy([("/assets/a.js", "script"), ("/assets/a.js", "style")]));
        Assert.Throws<ArgumentException>(() => new PluginSandboxResourcePolicy([("/assets/a.js", "fetch")]));
        var policy = new PluginSandboxResourcePolicy();
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/assets/a.js"), "script"));
        Assert.False(policy.Allows(new Uri("relative", UriKind.Relative), "script"));
        Assert.False(policy.Allows(new Uri("https://plugin.invalid/host/index.html"), "unknown"));
    }


    [Fact]
    public void BootstrapUrlWithUserInformationIsDenied()
    {
        var uri = new UriBuilder("https", "plugin.invalid", -1, "/host/index.html") { UserName = "fixture" }.Uri;
        Assert.False(new PluginSandboxResourcePolicy().Allows(uri, "top-navigation"));
    }
}
