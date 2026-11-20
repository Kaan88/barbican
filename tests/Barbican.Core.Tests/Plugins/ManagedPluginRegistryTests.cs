using System.Text.Json;
using Barbican.Core.Plugins.Packages.Managed;

namespace Barbican.Core.Tests.Plugins;

public sealed class ManagedPluginRegistryTests
{
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Row(string id, string extra = "") => $$"""{"id":"{{id}}","name":"Example","version":"1.0.0","author":"Example","description":"","type":"hook","entrypoint":"index.js","bundleHash":"{{Hash}}","permissions":[]{{extra}}}""";
    private static Barbican.Core.Common.Result<ManagedPluginRegistry> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ManagedPluginRegistry.Parse(document.RootElement);
    }

    [Fact]
    public void Valid_row_survives_disposal_and_ineligible_rows_do_not_poison_it()
    {
        var result = Parse($$"""{"plugins":[{{Row("good-plugin", ",\"settingsSchema\":{\"enabled\":{\"type\":\"boolean\"}}")}},{{Row("dev-plugin", ",\"dev\":true")}},{{Row("refused-plugin", ",\"refused\":true")}}],"themes":[]}""");
        Assert.True(result.IsOk);
        var entry = result.Value!.FindEligible("good-plugin")!;
        Assert.Equal(Hash, entry.BundleHash);
        Assert.Equal("boolean", entry.SettingsSchema!.Value.GetProperty("enabled").GetProperty("type").GetString());
        Assert.Null(result.Value.FindEligible("dev-plugin"));
        Assert.Null(result.Value.FindEligible("refused-plugin"));
    }

    [Fact]
    public void Duplicate_ids_or_nested_properties_reject_the_registry()
    {
        Assert.False(Parse($$"""{"plugins":[{{Row("same-id")}},{{Row("same-id")}}]}""").IsOk);
        Assert.False(Parse($$"""{"plugins":[{{Row("valid-id", ",\"locales\":{\"en\":{\"a\":\"x\",\"a\":\"y\"}}")}}]}""").IsOk);
    }

    [Theory]
    [InlineData(",\"tier\":\"privileged\"")]
    [InlineData(",\"httpOrigins\":[\"https://example.org/path\"]")]
    [InlineData(",\"apiPostPaths\":[\"/api/../secret\"]")]
    [InlineData(",\"dev\":\"false\"")]
    public void Invalid_authority_fields_are_ineligible(string extra)
    {
        var result = Parse($$"""{"plugins":[{{Row("valid-id", extra)}},{{Row("other-id")}}]}""");
        Assert.True(result.IsOk);
        Assert.Null(result.Value!.FindEligible("valid-id"));
        Assert.NotNull(result.Value.FindEligible("other-id"));
    }

    [Fact]
    public void Privileged_requires_crypto_full()
    {
        var row = Row("valid-id", ",\"tier\":\"privileged\",\"dev\":false").Replace("\"permissions\":[]", "\"permissions\":[\"crypto:full\"]", StringComparison.Ordinal);
        Assert.NotNull(Parse($$"""{"plugins":[{{row}}]}""").Value!.FindEligible("valid-id"));
        Assert.Null(Parse($$"""{"plugins":[{{row.Replace(",\"dev\":false", "", StringComparison.Ordinal)}}]}""").Value!.FindEligible("valid-id"));
    }
}
