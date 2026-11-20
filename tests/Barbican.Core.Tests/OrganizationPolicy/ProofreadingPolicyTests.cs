// Source: Bulwark lib/proofreading/gate.ts:42-65 and lib/admin/policy-scopes.ts:247-256.
using System.Text.Json;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class ProofreadingPolicyTests
{
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void ScopedLockAndGlobalLockDetermineAccess(bool global, bool scoped, bool expected)
    {
        const string source = """
            {"organisation":{},"restrictions":{"proofreading":{"locked":true,"value":GLOBAL}},
             "scopes":[{"id":"group","match":{"domains":["example.org"]},"policy":{
                "restrictions":{"proofreading.mock":{"locked":true,"value":SCOPED}}}}]}
            """;
        using var json = JsonDocument.Parse(source.Replace("GLOBAL", JsonSerializer.Serialize(global), StringComparison.Ordinal)
            .Replace("SCOPED", JsonSerializer.Serialize(scoped), StringComparison.Ordinal));
        var parsed = OrganizationPolicyParser.Parse(json.RootElement);
        Assert.True(parsed.IsOk);
        var policy = OrganizationPolicyResolver.Resolve(parsed.Value!, new("synthetic@example.org", []));
        Assert.Equal(expected, policy.Proofreading.Allows("mock", userChoice: false, perPluginChoice: null));
    }

    [Fact]
    public void ScopedDefaultsAndExplicitChoicesRespectOrder()
    {
        using var json = JsonDocument.Parse("""
            {"organisation":{},"defaults":{"proofreading":false,"proofreading.mock":false},
             "scopes":[{"id":"group","match":{"domains":["example.org"]},"policy":{"defaults":{"proofreading.mock":true}}}]}
            """);
        var parsed = OrganizationPolicyParser.Parse(json.RootElement);
        Assert.True(parsed.IsOk);
        var policy = OrganizationPolicyResolver.Resolve(parsed.Value!, new("synthetic@example.org", []));
        Assert.True(policy.Proofreading.Allows("mock", userChoice: false, perPluginChoice: null));
        Assert.False(policy.Proofreading.Allows("mock", userChoice: true, perPluginChoice: false));
        Assert.False(policy.Proofreading.Allows("other", userChoice: null, perPluginChoice: null));
    }
}
