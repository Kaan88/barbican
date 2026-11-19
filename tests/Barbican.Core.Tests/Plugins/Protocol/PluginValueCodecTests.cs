using System.Text;
using System.Text.Json;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins.Protocol;

public sealed class PluginValueCodecTests
{
    [Fact]
    public void Json_types_and_reserved_marker_names_round_trip_without_becoming_control_nodes()
    {
        using var json = JsonDocument.Parse("""{"kind":"buffer","id":1,"v":[null,true,42,"x"]}""");
        var value = PluginValue.FromJson(json.RootElement);
        var copy = PluginValueCodec.Decode(PluginValueCodec.Encode(value));
        Assert.Equal("buffer", copy.Property("kind").StringValue);
        Assert.Equal(JsonValueKind.Object, copy.ToJson().ValueKind);
        Assert.Equal(JsonValueKind.True, copy.Property("v").Item(1).ToJson().ValueKind);
    }

    [Fact]
    public void Views_preserve_offset_type_and_shared_backing_buffer_including_empty()
    {
        var b = new PluginValueBuilder();
        var buffer = b.Buffer([1, 2, 3, 4]);
        var view = b.View(buffer, PluginViewType.Uint8, 1, 2);
        var empty = b.View(buffer, PluginViewType.DataView, 4, 0);
        var root = b.Array(view, empty, buffer);
        var value = PluginValueCodec.Decode(PluginValueCodec.Encode(b.Build(root)));
        Assert.Equal([2, 3], value.Item(0).BinaryBytes());
        Assert.Empty(value.Item(1).BinaryBytes());
        Assert.Equal(PluginViewType.Uint8, value.Item(0).ViewType);
        Assert.Equal(1, value.Item(0).ByteOffset);
        Assert.True(value.Item(0).BackingBuffer.SameNode(value.Item(2)));
        Assert.True(value.Item(1).BackingBuffer.SameNode(value.Item(2)));
        Assert.Throws<InvalidOperationException>(() => value.ToJson());
    }

    [Fact]
    public void Cycles_aliases_and_undefined_remain_distinct_from_null()
    {
        var b = new PluginValueBuilder();
        var root = b.Reserve();
        var u = b.Undefined();
        b.SetObject(root, ("self", root), ("u", u), ("n", b.Null()), ("alias", u));
        var copy = PluginValueCodec.Decode(PluginValueCodec.Encode(b.Build(root)));
        Assert.True(copy.SameNode(copy.Property("self")));
        Assert.True(copy.Property("u").SameNode(copy.Property("alias")));
        Assert.Equal(PluginValueKind.Undefined, copy.Property("u").Kind);
        Assert.Equal(PluginValueKind.Null, copy.Property("n").Kind);
        Assert.Throws<InvalidOperationException>(() => copy.ToJson());
    }

    [Fact]
    public void Buffer_and_blob_are_copied_and_never_expose_mutable_backing_storage()
    {
        byte[] bytes = [4, 5];
        var b = new PluginValueBuilder();
        var blob = b.Blob(bytes, "text/plain");
        var value = b.Build(blob);
        bytes[0] = 9;
        var exposed = value.BinaryBytes();
        exposed[0] = 8;
        Assert.Equal([4, 5], value.BinaryBytes());
        Assert.Equal("text/plain", PluginValueCodec.Decode(PluginValueCodec.Encode(value)).BlobType);
    }

    [Theory]
    [InlineData("{\"root\":0,\"nodes\":[{\"id\":0,\"kind\":\"array\",\"items\":[1]}]}")]
    [InlineData("{\"root\":0,\"nodes\":[{\"id\":0,\"kind\":\"null\"},{\"id\":0,\"kind\":\"null\"}]}")]
    [InlineData("{\"root\":0,\"nodes\":[{\"id\":0,\"kind\":\"mystery\"}]}")]
    [InlineData("{\"root\":0,\"nodes\":[{\"id\":0,\"kind\":\"buffer\",\"data\":\"a===\"}]}")]
    [InlineData("{\"root\":0,\"nodes\":[{\"id\":0,\"kind\":\"buffer\",\"data\":\"AQ==\"},{\"id\":1,\"kind\":\"view\",\"buffer\":0,\"view\":\"Uint8\",\"offset\":1,\"length\":2}]} ")]
    public void Rejects_malformed_unknown_dangling_and_out_of_bounds_graphs(string wire) =>
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(Encoding.UTF8.GetBytes(wire)));

    [Fact]
    public void Enforces_decoded_binary_and_wire_limits_and_explicitly_rejects_non_json()
    {
        var value = PluginValue.FromBinary(new byte[9]);
        var wire = PluginValueCodec.Encode(value);
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(wire, new PluginValueLimits(MaxBinaryBytes: 8)));
        Assert.Throws<PluginValueFormatException>(() => PluginValueCodec.Decode(wire, new PluginValueLimits(MaxWireBytes: 8)));
        Assert.Throws<InvalidOperationException>(() => value.ToJson());
    }
}
