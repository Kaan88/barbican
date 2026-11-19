using System.Text.Json;
using Barbican.Core.Plugins.Protocol;
using Barbican.Core.Plugins.Protocol.Messages;

namespace Barbican.Core.Tests.Plugins.Protocol.Messages;

public sealed class PluginMessageSchemaTests
{
    public static TheoryData<PluginMessageDirection, string> Fixtures => new()
    {
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"sandbox-ready\"}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"init-done\",\"hooks\":[],\"slots\":[],\"shortcuts\":[]}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"init-error\",\"error\":\"bad\"}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"api-request\",\"id\":\"1\",\"method\":\"x\",\"args\":[]}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"callback-invoke\",\"id\":\"1\",\"callbackId\":\"2\",\"args\":[]}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"hook-result\",\"id\":\"1\",\"ok\":true}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"theme-applied\",\"dark\":true}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"slot-resize\",\"height\":-1}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"frame-escape\"}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"slot-should-show-result\",\"id\":\"1\",\"show\":false}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"floating-dropdown-open\",\"id\":\"1\",\"rect\":{\"left\":0,\"top\":0,\"bottom\":0,\"width\":0,\"height\":0},\"options\":[],\"selectedValues\":[],\"highlightedIndex\":-1}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"floating-dropdown-update\",\"id\":\"1\"}" },
        { PluginMessageDirection.SandboxToHost, "{\"type\":\"floating-dropdown-close\",\"id\":\"1\"}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"init\",\"payload\":{\"mode\":\"background\",\"pluginId\":\"p\",\"tier\":\"untrusted\",\"manifest\":{\"id\":\"p\",\"version\":\"1\",\"permissions\":[],\"settings\":{}},\"code\":\"\",\"locale\":\"en\"}}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"api-response\",\"id\":\"1\",\"ok\":true}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"callback-response\",\"id\":\"1\",\"ok\":false,\"error\":\"bad\"}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"hook-invoke\",\"id\":\"1\",\"hookName\":\"x\",\"args\":[]}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"locale-change\",\"locale\":\"en\"}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"theme-change\",\"theme\":{\"dark\":false,\"fontFamily\":\"sans\",\"vars\":{}}}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"settings-change\",\"settings\":{}}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"host-change\",\"features\":{\"externalSenderLabel\":true,\"externalRecipientWarning\":false,\"noThirdPartyTraffic\":true}}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"props-update\",\"props\":{}}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"slot-should-show\",\"id\":\"1\",\"slot\":\"reader-header\",\"context\":null}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"stream-end\",\"streamId\":\"1\"}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"stream-progress\",\"streamId\":\"1\",\"loaded\":0,\"total\":0}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"floating-dropdown-select\",\"id\":\"1\",\"option\":{\"value\":\"v\",\"label\":\"V\"}}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"floating-dropdown-highlight\",\"id\":\"1\",\"index\":-1}" },
        { PluginMessageDirection.HostToSandbox, "{\"type\":\"floating-dropdown-dismiss\",\"id\":\"1\"}" },
    };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ReferenceFixturesHaveExactlyOneDirection(PluginMessageDirection direction, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var value = PluginValue.FromJson(doc.RootElement);
        Assert.True(PluginMessageValidator.Validate(value, direction));
        Assert.False(PluginMessageValidator.Validate(value, direction == PluginMessageDirection.HostToSandbox ? PluginMessageDirection.SandboxToHost : PluginMessageDirection.HostToSandbox));
    }

    [Fact]
    public void StreamChunkRequiresArrayBuffer()
    {
        var b = new PluginValueBuilder();
        var root = b.Object(("type", b.String("stream-chunk")), ("streamId", b.String("1")), ("chunk", b.Buffer([])));
        Assert.True(PluginMessageValidator.Validate(b.Build(root), PluginMessageDirection.HostToSandbox));
    }
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void RequiredFieldsCannotBeRemovedOrChangedToArrays(PluginMessageDirection direction, string json)
    {
        var original = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        foreach (var name in original.Select(pair => pair.Key).Where(name => !string.Equals(name, "error", StringComparison.Ordinal) || string.Equals(original["type"]!.GetValue<string>(), "init-error", StringComparison.Ordinal)))
        {
            var removed = original.DeepClone().AsObject();
            removed.Remove(name);
            Assert.False(PluginMessageValidator.Validate(PluginValue.FromJson(JsonSerializer.SerializeToElement(removed)), direction));
            if (name is "args" or "hooks" or "slots" or "shortcuts" or "options" or "selectedValues" or "context") continue;
            var changed = original.DeepClone().AsObject();
            changed[name] = new System.Text.Json.Nodes.JsonArray();
            Assert.False(PluginMessageValidator.Validate(PluginValue.FromJson(JsonSerializer.SerializeToElement(changed)), direction));
        }
    }

    [Fact]
    public void LimitsAndUnknownTypesFailClosed()
    {
        static PluginValue Parse(string json) { using var doc = JsonDocument.Parse(json); return PluginValue.FromJson(doc.RootElement); }
        var direction = PluginMessageDirection.SandboxToHost;
        Assert.False(PluginMessageValidator.Validate(Parse("{\"type\":\"unknown\"}"), direction));
        Assert.False(PluginMessageValidator.Validate(Parse("{\"type\":\"api-request\",\"id\":\"\",\"method\":\"x\",\"args\":[]}"), direction));
        Assert.False(PluginMessageValidator.Validate(Parse("{\"type\":\"api-request\",\"id\":\"12\",\"method\":\"x\",\"args\":[]}"), direction, new(MaxIdLength: 1)));
        Assert.False(PluginMessageValidator.Validate(Parse("{\"type\":\"api-request\",\"id\":\"1\",\"method\":\"x\",\"args\":[1,2]}"), direction, new(MaxSequenceLength: 1)));
        Assert.False(PluginMessageValidator.Validate(Parse("{\"type\":\"sandbox-ready\",\"extra\":[1,2]}"), direction, new(MaxGraphNodes: 2)));
        Assert.False(PluginMessageValidator.Validate(Parse("{\"type\":\"sandbox-ready\"}"), (PluginMessageDirection)99));
        Assert.True(PluginMessageValidator.Validate(Parse("{\"type\":\"sandbox-ready\",\"packageId\":\"claim\"}"), direction));
    }
    [Theory]
    [InlineData("{\"type\":\"init-done\",\"hooks\":[],\"slots\":[{\"name\":\"bad\",\"hasShouldShow\":true,\"order\":0}],\"shortcuts\":[]}")]
    [InlineData("{\"type\":\"init-done\",\"hooks\":[],\"slots\":[{\"name\":\"reader-header\",\"hasShouldShow\":1,\"order\":0}],\"shortcuts\":[]}")]
    [InlineData("{\"type\":\"floating-dropdown-open\",\"id\":\"1\",\"rect\":{\"left\":0,\"top\":0,\"bottom\":0,\"width\":0,\"height\":false},\"options\":[],\"selectedValues\":[],\"highlightedIndex\":-1}")]
    [InlineData("{\"type\":\"api-request\",\"id\":\"1\",\"method\":\"x\",\"args\":false}")]
    public void InvalidNestedShapesFail(string json)
    {
        using var doc = JsonDocument.Parse(json);
        Assert.False(PluginMessageValidator.Validate(PluginValue.FromJson(doc.RootElement), PluginMessageDirection.SandboxToHost));
    }

    [Fact]
    public void SlotInitAndStreamChunkRequireTheirOwnFields()
    {
        const string json = "{\"type\":\"init\",\"payload\":{\"mode\":\"slot\",\"pluginId\":\"p\",\"tier\":\"privileged\",\"slot\":\"reader-header\",\"manifest\":{\"id\":\"p\",\"version\":\"1\",\"permissions\":[],\"settings\":{}},\"code\":\"\",\"locale\":\"en\",\"extraProps\":{},\"theme\":{\"dark\":false,\"fontFamily\":\"sans\",\"vars\":{}}}}";
        var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(PluginMessageValidator.Validate(PluginValue.FromJson(JsonSerializer.SerializeToElement(root)), PluginMessageDirection.HostToSandbox));
        root["payload"]!.AsObject().Remove("theme");
        Assert.False(PluginMessageValidator.Validate(PluginValue.FromJson(JsonSerializer.SerializeToElement(root)), PluginMessageDirection.HostToSandbox));
        using var badChunk = JsonDocument.Parse("{\"type\":\"stream-chunk\",\"streamId\":\"1\",\"chunk\":[]}");
        Assert.False(PluginMessageValidator.Validate(PluginValue.FromJson(badChunk.RootElement), PluginMessageDirection.HostToSandbox));
    }
}
