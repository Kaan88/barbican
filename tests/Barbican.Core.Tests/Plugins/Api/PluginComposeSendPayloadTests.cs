using Barbican.Core.Compose;
using Barbican.Core.Plugins.Api.ComposeSend;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class PluginComposeSendPayloadTests
{
    [Fact]
    public void ProjectsFullSdkRequestAndPreservesMultipartAttachments()
    {
        var input = Sample() with
        {
            InReplyTo = "parent", References = ["root", "parent"], DelayedUntil = "2030-01-02T03:04:05.000Z", LabelToken = "host-token",
            Attachments = [new DraftAttachment { Name = "one.txt", Type = "text/plain", Size = 42, BlobId = "blob-1" },
                new DraftAttachment { Name = "inline.png", Type = "image/png", Size = 101, BlobId = "blob-2", Cid = "cid-2" }],
        };
        var args = PluginComposeSendPayload.Encode(input);
        Assert.Equal(PluginValueKind.Array, args.Kind);
        Assert.Equal(1, args.Count);
        var value = args.Item(0);
        Assert.Equal("Alice <a@example.org>", value.Property("to").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal("bcc@example.org", value.Property("bcc").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal("<p>html</p>", value.Property("htmlBody").StringValue, StringComparer.Ordinal);
        Assert.Equal("text", value.Property("textBody").StringValue, StringComparer.Ordinal);
        Assert.Equal("Sender", value.Property("fromName").StringValue, StringComparer.Ordinal);
        Assert.Equal("parent", value.Property("inReplyTo").StringValue, StringComparer.Ordinal);
        Assert.Equal("root", value.Property("references").Item(0).StringValue, StringComparer.Ordinal);
        Assert.Equal("2030-01-02T03:04:05.000Z", value.Property("delayedUntil").StringValue, StringComparer.Ordinal);
        Assert.Equal("host-token", value.Property("labelToken").StringValue, StringComparer.Ordinal);
        var attachments = value.Property("attachments");
        Assert.Equal(2, attachments.Count);
        Assert.Equal("42", attachments.Item(0).Property("size").NumberText, StringComparer.Ordinal);
        Assert.Equal(PluginValueKind.Undefined, attachments.Item(0).Property("cid").Kind);
        Assert.Equal("blob-2", attachments.Item(1).Property("blobId").StringValue, StringComparer.Ordinal);
        Assert.Equal("cid-2", attachments.Item(1).Property("cid").StringValue, StringComparer.Ordinal);
        Assert.DoesNotContain("accountId", value.PropertyNames, StringComparer.Ordinal);
    }

    [Fact]
    public void AbsentOptionalFieldsAreUndefinedAndEmptyListsRemainArrays()
    {
        var value = PluginComposeSendPayload.Encode(Sample()).Item(0);
        Assert.Equal(PluginValueKind.Undefined, value.Property("inReplyTo").Kind);
        Assert.Equal(PluginValueKind.Undefined, value.Property("references").Kind);
        Assert.Equal(PluginValueKind.Undefined, value.Property("labelToken").Kind);
        Assert.Equal(PluginValueKind.Undefined, value.Property("delayedUntil").Kind);
        Assert.Equal(0, value.Property("cc").Count);
        Assert.Equal(0, value.Property("attachments").Count);
    }

    [Fact]
    public void RejectsMissingBlobAndInexactAttachmentSize()
    {
        var input = Sample();
        Assert.Throws<PluginValueFormatException>(() => PluginComposeSendPayload.Encode(input with { Attachments = [new DraftAttachment { Name = "pending" }] }));
        Assert.Throws<PluginValueFormatException>(() => PluginComposeSendPayload.Encode(input with { Attachments = [new DraftAttachment { Name = "big", BlobId = "blob", Size = 9_007_199_254_740_992 }] }));
        Assert.Throws<PluginValueFormatException>(() => PluginComposeSendPayload.Encode(input with { To = Enumerable.Repeat("x@example.org", 201).ToArray() }));
    }

    private static PluginComposeSendRequest Sample() => new(["Alice <a@example.org>"], [], ["bcc@example.org"], "subject", "<p>html</p>", "text",
        "identity", "sender@example.org", "Sender", null, null, null, null, []);
}
