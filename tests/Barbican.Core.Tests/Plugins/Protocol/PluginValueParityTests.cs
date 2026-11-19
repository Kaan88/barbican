// Cross-language vectors for the native v1 transport adaptation of Bulwark protocol.ts:115-334.
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins.Protocol;

public sealed class PluginValueParityTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(RepoFiles.Root, "tools", "tests", name));

    [Fact]
    public void JavaScript_encoded_fixture_decodes_in_core_with_identity_and_binary_intact()
    {
        var value = PluginValueCodec.Decode(Fixture("plugin-value-js-golden.json"));
        Assert.Equal("JS fixture", value.Property("name").StringValue);
        Assert.True(value.SameNode(value.Property("self")));
        Assert.True(value.Property("view").BackingBuffer.SameNode(value.Property("buffer")));
        Assert.Equal([20, 30], value.Property("view").BinaryBytes());
        Assert.Empty(value.Property("empty").BinaryBytes());
        Assert.Equal(PluginValueKind.Undefined, value.Property("missing").Kind);
        Assert.Equal(PluginValueKind.Null, value.Property("nul").Kind);
        Assert.Equal("-0", value.Property("negativeZero").NumberText);
        Assert.Equal("12345678901234567890", value.Property("big").NumberText);
        Assert.Equal("\ud800\udc00\ud800\0", value.Property("weird").StringValue);
        Assert.Equal([8, 9], value.Property("blob").BinaryBytes());
        Assert.Equal("text/plain", value.Property("blob").BlobType);
        Assert.True(value.Property("map").MapEntry(0).Value.SameNode(value));
        Assert.True(value.Property("set").Item(0).SameNode(value.Property("view")));
        Assert.Equal("safe", value.Property("__proto__").StringValue);
        Assert.Equal("1234", value.Property("date").NumberText);
        Assert.Equal("TypeError", value.Property("error").ErrorValue.Name);
        Assert.True(value.Property("error").ErrorValue.Cause!.SameNode(value));
    }

    [Theory]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0.5)]
    [InlineData(8640000000000001d)]
    public void Date_values_that_javascript_timeclip_cannot_preserve_are_rejected(double value)
    {
        var b = new PluginValueBuilder();
        Assert.Throws<PluginValueFormatException>(() => b.Build(b.Date(value)));
    }

    [Fact]
    public void Negative_zero_date_is_rejected_because_timeclip_normalizes_it()
    {
        var b = new PluginValueBuilder();
        Assert.Throws<PluginValueFormatException>(() => b.Build(b.Date(BitConverter.Int64BitsToDouble(long.MinValue))));
    }

    [Theory]
    [InlineData("Text/Plain")]
    [InlineData("text/\u0001plain")]
    [InlineData("text/é")]
    public void Blob_mime_that_javascript_would_normalize_is_rejected(string type)
    {
        var b = new PluginValueBuilder();
        Assert.Throws<PluginValueFormatException>(() => b.Build(b.Blob([], type)));
    }

    [Fact]
    public void Date_nan_and_boundary_and_canonical_blob_mime_are_supported()
    {
        var b = new PluginValueBuilder();
        Assert.Equal("NaN", b.Build(b.Date(double.NaN)).NumberText);
        var c = new PluginValueBuilder();
        Assert.Equal("8640000000000000", c.Build(c.Date(8640000000000000d)).NumberText);
        var d = new PluginValueBuilder();
        Assert.Equal("text/plain", d.Build(d.Blob([], "text/plain")).BlobType);
    }

    [Fact]
    public void Core_encoded_fixture_matches_the_vector_consumed_by_javascript()
    {
        var b = new PluginValueBuilder();
        var root = b.Reserve();
        var name = b.String("C# fixture");
        var data = b.Buffer([1, 2, 3, 4]);
        var view = b.View(data, PluginViewType.Uint8, 1, 2);
        var undefined = b.Undefined();
        var big = b.BigInt(BigInteger.Parse("12345678901234567890", CultureInfo.InvariantCulture));
        var blob = b.Blob([8, 9], "text/plain");
        var error = b.Error("TypeError", "fixture error", "fixture stack", root);
        b.SetObject(root, ("name", name), ("data", data), ("view", view), ("self", root), ("undef", undefined), ("special", big), ("blob", blob), ("error", error));
        var actual = JsonNode.Parse(PluginValueCodec.Encode(b.Build(root)));
        var expected = JsonNode.Parse(Fixture("plugin-value-csharp-golden.json"));
        Assert.True(JsonNode.DeepEquals(expected, actual));
    }
}
