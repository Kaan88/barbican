// Source-derived policy tests: Bulwark proxy.ts:365-480, host-bridge.ts:356-370; M6b security invariants.
using Barbican.App.Plugins.Sandbox;
namespace Barbican.Automation.Tests.Plugins;
public sealed class PluginSandboxPolicyRedTests
{
    [Red("m6b-runtime")]
    [Fact]
    public void Untrusted_frame_retains_form_events_but_cannot_submit_or_gain_same_origin()
    {
        var policy = new PluginSandboxResourcePolicy();
        Assert.Contains("allow-scripts", policy.SandboxFlags, StringComparison.Ordinal);
        Assert.Contains("allow-forms", policy.SandboxFlags, StringComparison.Ordinal);
        Assert.DoesNotContain("allow-same-origin", policy.SandboxFlags, StringComparison.Ordinal);
        Assert.Contains("form-action 'none'", policy.ContentSecurityPolicy, StringComparison.Ordinal);
    }
    [Red("m6b-runtime")]
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
}
