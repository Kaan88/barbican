using System.Numerics;
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins.Protocol;

public sealed class PluginValueAdversarialTests
{
    private static JsonObject Wire(PluginValue value) => JsonNode.Parse(PluginValueCodec.Encode(value))!.AsObject();
    private static byte[] Bytes(JsonObject value) => System.Text.Encoding.UTF8.GetBytes(value.ToJsonString());

    [Fact]
    public void Extended_clone_kinds_and_plain_aliases_are_distinct()
    {
        var b = new PluginValueBuilder();
        var date = b.Date(1234);
        var bigint = b.BigInt(BigInteger.Parse("123456789012345678901234567890", System.Globalization.CultureInfo.InvariantCulture));
        var text = b.String("x");
        var map = b.Map((text, bigint));
        var set = b.Set(date, text);
        var error = b.Error("TypeError", "bad", "stack", date);
        var value = PluginValueCodec.Decode(PluginValueCodec.Encode(b.Build(b.Array(date, bigint, map, set, error))));
        Assert.Equal(PluginValueKind.Date, value.Item(0).Kind);
        Assert.Equal("123456789012345678901234567890", value.Item(1).NumberText);
        Assert.Equal(PluginValueKind.Map, value.Item(2).Kind);
        Assert.True(value.Item(2).MapEntry(0).Value.SameNode(value.Item(1)));
        Assert.True(value.Item(3).Item(0).SameNode(value.Item(0)));
        Assert.True(value.Item(4).ErrorValue.Cause!.SameNode(value.Item(0)));
        Assert.Throws<InvalidOperationException>(() => value.ToJson());
        var c = new PluginValueBuilder();
        var shared = c.Object(("x", c.Number(1)));
        Assert.Equal("[{\"x\":1},{\"x\":1}]", c.Build(c.Array(shared, shared)).ToJson().GetRawText());
    }

    [Fact]
    public void Decoder_rejects_valid_envelope_with_invalid_reference_and_duplicate_id()
    {
        var b = new PluginValueBuilder();
        var value = b.Build(b.Array(b.Null()));
        var wire = Wire(value);
        wire["nodes"]![1]!["links"]![0] = 300;
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
        wire = Wire(value);
        wire["nodes"]![1]!["id"] = 0;
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
    }

    [Fact]
    public void Decoder_rejects_bad_view_range_wrong_backing_and_malformed_binary()
    {
        var b = new PluginValueBuilder();
        var buffer = b.Buffer([1, 2, 3, 4]);
        var value = b.Build(b.View(buffer, PluginViewType.Uint8, 1, 2));
        var wire = Wire(value);
        wire["nodes"]![1]!["length"] = 5;
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
        wire = Wire(value);
        wire["nodes"]![0]!["kind"] = "Null";
        wire["nodes"]![0]!["bytes"] = null;
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
        wire = Wire(value);
        wire["nodes"]![0]!["bytes"] = "a===";
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
    }

    [Fact]
    public void Truncation_unknown_kind_duplicate_field_and_string_budget_are_rejected()
    {
        var value = PluginValue.FromBinary([1, 2, 3]);
        var encoded = PluginValueCodec.Encode(value);
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(encoded.AsMemory(0, encoded.Length - 1)));
        var wire = Wire(value);
        wire["nodes"]![0]!["kind"] = "Unknown";
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
        var duplicate = "{\"version\":1,\"version\":1,\"root\":0,\"nodes\":[]}";
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(System.Text.Encoding.UTF8.GetBytes(duplicate)));
        var b = new PluginValueBuilder();
        var text = b.Build(b.String("four"));
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(PluginValueCodec.Encode(text), new PluginValueLimits(MaxStringBytes: 3)));
    }

    [Fact]
    public void Json_expansion_of_shared_values_is_bounded()
    {
        var b = new PluginValueBuilder();
        var text = b.String(new string('x', 50));
        var array = b.Array(text, text, text, text);
        var value = b.Build(array, new PluginValueLimits(MaxWireBytes: 100));
        Assert.Throws<InvalidOperationException>(() => value.ToJson());
    }

    [Fact]
    public void Json_import_limits_and_nonfinite_numbers_fail_without_silent_conversion()
    {
        using var doc = System.Text.Json.JsonDocument.Parse("[[[null]]]");
        Assert.Throws<PluginValueFormatException>(() => PluginValue.FromJson(doc.RootElement, new PluginValueLimits(MaxDepth: 2)));
        var c = new PluginValueBuilder();
        var nan = c.Build(c.Number(double.NaN));
        Assert.Equal("NaN", PluginValueCodec.Decode(PluginValueCodec.Encode(nan)).NumberText);
        Assert.Throws<InvalidOperationException>(() => nan.ToJson());
    }

    [Fact]
    public void Lone_surrogates_pairs_nul_and_keys_round_trip_losslessly()
    {
        const string high = "\ud800";
        const string low = "\udc00";
        const string pairAndNul = "\ud83d\ude00\0";
        var b = new PluginValueBuilder();
        var root = b.Object((high, b.String(low)), (pairAndNul, b.String(pairAndNul)));
        var copy = PluginValueCodec.Decode(PluginValueCodec.Encode(b.Build(root)));
        Assert.Equal(low, copy.Property(high).StringValue);
        Assert.Equal(pairAndNul, copy.Property(pairAndNul).StringValue);
        var fromJson = PluginValue.FromJson(copy.ToJson());
        Assert.Equal(low, fromJson.Property(high).StringValue);
        Assert.Equal(pairAndNul, fromJson.Property(pairAndNul).StringValue);
        using var raw = System.Text.Json.JsonDocument.Parse("{\"\\ud800\":\"\\udc00\"}");
        Assert.Equal(low, PluginValue.FromJson(raw.RootElement).Property(high).StringValue);
    }

    [Fact]
    public void Tagged_utf16_rejects_odd_lengths_invalid_base64_and_string_budget()
    {
        var b = new PluginValueBuilder();
        var value = b.Build(b.String("\ud800"));
        var wire = Wire(value);
        wire["nodes"]![0]!["text"]!["utf16"] = "AQ==";
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
        wire = Wire(value);
        wire["nodes"]![0]!["text"]!["utf16"] = "a===";
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Bytes(wire)));
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(PluginValueCodec.Encode(value), new PluginValueLimits(MaxStringBytes: 1)));
    }

    [Fact]
    public void Depth_and_sequence_limits_apply_to_real_graph_references()
    {
        var b = new PluginValueBuilder();
        var leaf = b.Null();
        var middle = b.Array(leaf);
        var root = b.Array(middle);
        var wire = PluginValueCodec.Encode(b.Build(root));
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(wire, new PluginValueLimits(MaxDepth: 2)));
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(wire, new PluginValueLimits(MaxSequenceLength: 0)));
    }
}
