// Bulwark lib/__tests__/plugin-device-policy.test.ts and lib/admin/__tests__/policy-scopes.test.ts.
using System.Text.Json;
using Barbican.Core.OrganizationPolicy;

namespace Barbican.Core.Tests.OrganizationPolicy;

public sealed class OrganizationPolicyTests
{
    [Fact]
    public void FullEmptyEnvelopeIsKnownClearButPartialAndMalformedAreNot()
    {
        var clear = Parse("""{"organisation":{},"scopes":[]}""");
        Assert.True(clear.IsOk);
        var effective = OrganizationPolicyResolver.Resolve(clear.Value!, new("user@example.org", []));
        Assert.False(effective.NoPrint);
        Assert.False(effective.Watermark);
        Assert.Empty(effective.Labels.List);
        Assert.False(Parse("""{"scopes":[]}""").IsOk);
        Assert.False(Parse("""{"organisation":{"devices":{"noPrint":"false"}},"scopes":[]}""").IsOk);
        Assert.False(Parse("""{"organisation":{},"scopes":null}""").IsOk);
        Assert.False(Parse("""{"organisation":{"devices":{"noPrint":false,"noPrint":true}},"scopes":[]}""").IsOk);
    }

    [Fact]
    public void DomainThenGroupPrecedenceAndWholeLabelReplacement()
    {
        var doc = Parse("""
        {"organisation":{"devices":{"noPrint":true},"labels":{"list":[{"id":"global","name":"Global"}],"defaultId":"global","required":true}},
         "scopes":[
          {"id":"group","match":{"groups":["Team@Example.org"]},"policy":{"organisation":{"devices":{"noPrint":false},"labels":{"list":[{"id":"team","name":"Team"}]}}}},
          {"id":"domain","match":{"domains":["*.example.org"]},"policy":{"organisation":{"devices":{"watermark":true}}}},
          {"id":"both","match":{"domains":["child.example.org"],"groups":["team@example.org"]},"policy":{"organisation":{"devices":{"noPrint":true}}}}
         ]}
        """);
        Assert.True(doc.IsOk);
        var policy = OrganizationPolicyResolver.Resolve(doc.Value!, new("a@child.example.org", ["team@example.org"]));
        Assert.Equal(["domain", "both", "group"], policy.MatchedScopes, StringComparer.Ordinal);
        Assert.False(policy.NoPrint);
        Assert.True(policy.Watermark);
        Assert.Equal("group", policy.NoPrintSource, StringComparer.Ordinal);
        Assert.Equal("team", Assert.Single(policy.Labels.List).Id, StringComparer.Ordinal);
        Assert.False(policy.Labels.Required);
        Assert.Equal("", policy.Labels.DefaultId, StringComparer.Ordinal);
    }

    [Fact]
    public void SenderGroupGetsOnlyItsGroupScopesPersonalAliasKeepsAccountScopes()
    {
        var doc = Parse("""
        {"organisation":{},"scopes":[
          {"id":"one","match":{"groups":["one@example.org"]},"policy":{"organisation":{"devices":{"noPrint":true}}}},
          {"id":"two","match":{"groups":["two@example.org"]},"policy":{"organisation":{"devices":{"noPrint":false,"watermark":true}}}}
        ]}
        """).Value!;
        var subject = new OrganizationPolicySubject("me@example.org", ["one@example.org", "two@example.org"]);
        Assert.True(OrganizationPolicyResolver.ResolveForSender(doc, subject, "ONE@example.org").NoPrint);
        var personal = OrganizationPolicyResolver.ResolveForSender(doc, subject, "alias@example.net");
        Assert.False(personal.NoPrint);
        Assert.True(personal.Watermark);
    }

    [Fact]
    public void InternationalizedSubjectsScopesAndGroupsMatchAsciiEquivalents()
    {
        var parsed = Parse("""
        {"organisation":{},"scopes":[
          {"id":"domain","match":{"domains":["*.bücher.example"]},"policy":{"organisation":{"devices":{"noPrint":true}}}},
          {"id":"group","match":{"groups":["team@bücher.example"]},"policy":{"organisation":{"devices":{"watermark":true}}}}
        ]}
        """);
        Assert.True(parsed.IsOk);
        var unicode = OrganizationPolicyResolver.ResolveForSender(parsed.Value!, new("user@child.bücher.example", ["team@xn--bcher-kva.example"]), "team@bücher.example");
        var ascii = OrganizationPolicyResolver.ResolveForSender(parsed.Value!, new("user@child.xn--bcher-kva.example", ["team@bücher.example"]), "team@xn--bcher-kva.example");
        Assert.True(unicode.NoPrint);
        Assert.True(unicode.Watermark);
        Assert.Equal(unicode.NoPrint, ascii.NoPrint);
        Assert.Equal(unicode.Watermark, ascii.Watermark);
        Assert.Equal(unicode.MatchedScopes, ascii.MatchedScopes);
    }

    [Theory]
    [InlineData("""{"organisation":{"labels":{"required":true,"list":[]}},"scopes":[]}""")]
    [InlineData("""{"organisation":{"labels":{"extraHeader":"From"}},"scopes":[]}""")]
    [InlineData("""{"organisation":{"labels":{"defaultId":"missing"}},"scopes":[]}""")]
    [InlineData("""{"organisation":{},"scopes":[{"id":"s","match":{"domains":["bad*"]},"policy":{}}]}""")]
    public void RejectsUnsafeOrAmbiguousPolicy(string json) => Assert.False(Parse(json).IsOk);

    private static Barbican.Core.Common.Result<OrganizationPolicyDocument> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return OrganizationPolicyParser.Parse(document.RootElement);
    }
}
