// Source: Bulwark lib/proofreading/segments.ts and annotations.ts:86-128.
using System.Text.Json;
using Barbican.Core.Editor;
using Barbican.Core.Plugins.Api.Composer;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Tests.Plugins.Api;

public sealed class CheckerTextTests
{
    [Fact]
    public void PayloadIncludesOnlyEditableAuthoredTextAndOpaqueIds()
    {
        var body = Block.Para("teh");
        body.Runs.Add(new Run("SIGNATURE", Marks.Plain) { IsComposerContext = true });
        body.Runs.Add(new Run("dem Brief", Marks.Plain));
        var doc = new Doc([body, Block.WithText(new BlockKind.CodeBlock(), "CODE", Marks.Plain),
            new Block(new BlockKind.Quoted("QUOTE HTML", "QUOTE TEXT", false, true))]);
        Assert.Null(PluginCheckRequest.Create(doc, "chk-test", "en"));
        doc.HasComposerProvenance = true;
        var request = PluginCheckRequest.Create(doc, "chk-test", "en")!;
        var payload = request.Arguments.Item(0);
        Assert.Equal("chk-test", payload.Property("composerId").StringValue, StringComparer.Ordinal);
        var segments = payload.Property("segments");
        Assert.Equal(2, segments.Count);
        Assert.Equal("teh", segments.Item(0).Property("text").StringValue, StringComparer.Ordinal);
        Assert.Equal("dem Brief", segments.Item(1).Property("text").StringValue, StringComparer.Ordinal);
    }

    [Fact]
    public void ValidatesUtf16AndAllowsOnlySafeReplacementTextAtTheIssuedRange()
    {
        var doc = Doc.FromText("😀 teh");
        doc.HasComposerProvenance = true;
        var request = PluginCheckRequest.Create(doc, "chk-test", "en")!;
        var id = request.Arguments.Item(0).Property("segments").Item(0).Property("id").StringValue;
        using var response = JsonDocument.Parse($$"""
          [null,{"segmentId":"{{id}}","offset":1,"length":1,"kind":"spelling","message":"Split surrogate"},
           {"segmentId":"{{id}}","offset":"3","length":3,"kind":"spelling","message":"Wrong type"},
           {"segmentId":"foreign","offset":3,"length":3,"kind":"spelling","message":"Foreign"},
           {"segmentId":"{{id}}","offset":3,"length":3,"kind":"spelling","message":"A typo","replacements":["the","x\u202ay","one\ntwo"]}]
          """);
        var issue = Assert.Single(request.Decode(PluginValue.FromJson(response.RootElement), ["spelling"]));
        Assert.Equal(new Pos(0, 3), issue.Start);
        Assert.Equal("teh", issue.Text, StringComparer.Ordinal);
        Assert.Equal("the", Assert.Single(issue.Replacements), StringComparer.Ordinal);

        using var optional = JsonDocument.Parse($$"""
          [{"segmentId":"{{id}}","offset":3,"length":3,"kind":"spelling","message":"A typo"}]
          """);
        Assert.Empty(Assert.Single(request.Decode(PluginValue.FromJson(optional.RootElement), ["spelling"])).Replacements);
    }
}
