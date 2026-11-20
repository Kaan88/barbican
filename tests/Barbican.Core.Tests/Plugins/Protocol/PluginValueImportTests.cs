// Host transport composition of Bulwark protocol.ts:115-334 structured-clone results.
using System.Text;
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Tests.Plugins.Protocol;
public sealed class PluginValueImportTests
{
    [Fact]
    public void Api_response_import_preserves_cycles_views_aliases_and_excludes_private_siblings()
    {
        byte[] bytes = [10,20,30,40];
        var source = new PluginValueBuilder();
        var buffer = source.Buffer(bytes);
        var view = source.View(buffer,PluginViewType.Uint8,1,2);
        var cycle = source.Reserve();
        source.SetArray(cycle,view,buffer,cycle,source.Undefined());
        var root = source.Object(("public",cycle),("private",source.String("PRIVATE_SIBLING_DO_NOT_EXPORT")));
        var graph = source.Build(root);
        bytes[1] = 99;

        var response = new PluginValueBuilder();
        var result = response.Import(graph.Property("public"));
        var bufferAgain = response.Import(graph.Property("public").Item(1));
        Assert.Equal(result,response.Import(graph.Property("public")));
        var envelope = response.Build(response.Object(("type",response.String("api-response")),("id",response.String("reply")),("ok",response.Boolean(true)),("result",result),("same",bufferAgain)));
        var value = envelope.Property("result");
        Assert.True(value.SameNode(value.Item(2)));
        Assert.True(value.Item(0).BackingBuffer.SameNode(value.Item(1)));
        Assert.True(value.Item(1).SameNode(envelope.Property("same")));
        Assert.Equal(new byte[] {20,30},value.Item(0).BinaryBytes());
        Assert.Equal(PluginValueKind.Undefined,value.Item(3).Kind);
        var wire = PluginValueCodec.Encode(envelope);
        Assert.DoesNotContain("PRIVATE_SIBLING_DO_NOT_EXPORT",Encoding.UTF8.GetString(wire),StringComparison.Ordinal);
        var roundTrip = PluginValueCodec.Decode(wire).Property("result");
        Assert.True(roundTrip.SameNode(roundTrip.Item(2)));
        Assert.Equal(new byte[] {20,30},roundTrip.Item(0).BinaryBytes());
    }
    [Fact]
    public void Import_respects_the_envelope_binary_limit()
    {
        var source = PluginValue.FromBinary(new byte[] {1,2,3,4});
        var builder = new PluginValueBuilder();
        var result = builder.Import(source);
        Assert.Throws<PluginValueFormatException>(() => builder.Build(result,new PluginValueLimits(MaxBinaryBytes:3)));
    }
}
