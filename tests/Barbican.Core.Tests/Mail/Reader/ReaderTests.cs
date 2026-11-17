// Ported from Rust src/reader.rs tests (:1216-:1387), the pure ones. Red until stream reader implements them (M2 plan 6.3, T-212..T-222).
// The one-click network tests are in Net/OneClickUnsubscribeTests.cs.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Net;

namespace Barbican.Core.Tests.Mail.Reader;

public sealed class ReaderTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static JsonArray Arr(string json) => (JsonArray)J(json);

    [Fact]
    [Red("reader")]
    [RustTest("reader::headers_unfold")]
    public void Headers_unfold()
    {
        var h = Arr("""[{"name": "List-Unsubscribe", "value": " <https://x.example/u?a=1>,\r\n\t<mailto:u@x.example>"}, {"name": "list-unsubscribe", "value": " second"}]""");
        Assert.Equal("<https://x.example/u?a=1>, <mailto:u@x.example>", ReaderHeaders.Header(h, "LIST-UNSUBSCRIBE"));
        Assert.Equal(2, ReaderHeaders.HeadersAll(h, "list-unsubscribe").Count);
        Assert.Null(ReaderHeaders.Header(h, "List-Id"));
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::unsubscribe_parsing")]
    public void Unsubscribe_parsing()
    {
        var u = UnsubscribeParser.Parse("<https://news.example/unsub?u=4f2a9c>, <mailto:unsubscribe@news.example?subject=unsubscribe>", "List-Unsubscribe=One-Click")!;
        Assert.Equal("https://news.example/unsub?u=4f2a9c", u.Http);
        Assert.Equal("mailto:unsubscribe@news.example?subject=unsubscribe", u.Mailto);
        Assert.True(u.OneClick && u.PrefersHttp);

        // one-click needs https and the exact token
        Assert.False(UnsubscribeParser.Parse("<http://n.example/u>", "List-Unsubscribe=One-Click")!.OneClick);
        Assert.False(UnsubscribeParser.Parse("<https://n.example/u>", null)!.OneClick);
        var m = UnsubscribeParser.Parse("<mailto:leave@list.example?subject=Leave%20me+now&body=bye>", null)!;
        Assert.False(m.PrefersHttp);
        Assert.Equal(new UnsubscribeMailto("leave@list.example", "Leave me+now", "bye"), UnsubscribeParser.ParseMailto(m.Mailto!));

        // refused: javascript links, no brackets, several or invalid recipients
        Assert.Null(UnsubscribeParser.Parse("<javascript:alert(1)>", null));
        Assert.Null(UnsubscribeParser.Parse("https://n.example/u", null));
        Assert.Null(UnsubscribeParser.Parse("<mailto:a@x.example,b@x.example>", null));
        Assert.Null(UnsubscribeParser.ParseMailto("mailto:not-an-address"));

        // subject made one line and capped, body capped
        var capped = UnsubscribeParser.ParseMailto("mailto:a@x.example?subject=a%0D%0Ab&body=" + new string('x', 600))!;
        Assert.Equal("a b", capped.Subject);
        Assert.Equal(500, capped.Body!.Length);
        Assert.True(UnsubscribeParser.ValidEmail("a.b+c@sub.example.org"));
        Assert.False(UnsubscribeParser.ValidEmail("a@b"));
        Assert.False(UnsubscribeParser.ValidEmail("a b@c.example"));
        Assert.False(UnsubscribeParser.ValidEmail("@c.example"));
        Assert.False(UnsubscribeParser.ValidEmail("a@@c.example"));
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::blob_copy_between_accounts")]
    public void Blob_copy_between_accounts()
    {
        Assert.Null(BlobCopyRequests.Request("a1", "a1", "B"));
        var r = BlobCopyRequests.Request("shared9", "a1", "B")!;
        Assert.Equal("Blob/copy", (string)r[0]![0]!);
        Assert.True(JsonNode.DeepEquals(J("""{"fromAccountId": "shared9", "accountId": "a1", "blobIds": ["B"]}"""), r[0]![1]));
        Assert.Equal("B2", BlobCopyRequests.Copied(J("""[["Blob/copy", {"copied": {"B": "B2"}}, "b"]]"""), "B").Value);
        Assert.False(BlobCopyRequests.Copied(J("""[["Blob/copy", {"copied": null, "notCopied": {"B": {"type": "blobNotFound"}}}, "b"]]"""), "B").IsOk);
        Assert.False(BlobCopyRequests.Copied(J("""[["error", {"type": "accountNotFound"}, "b"]]"""), "B").IsOk);
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::dkim_gate")]
    public async Task Dkim_gate()
    {
        var pass = Arr("""[{"name": "Authentication-Results", "value": " mx.example.org; dkim=pass header.d=news.example; spf=pass"}, {"name": "Authentication-Results", "value": " evil; dkim=fail"}]""");
        Assert.True(DkimGate.DkimPass(pass, "list@news.example"));
        Assert.False(DkimGate.DkimPass(pass, "ceo@bank.example"), "a pass for another domain does not vouch for From");
        var fail = Arr("""[{"name": "Authentication-Results", "value": " mx.example.org; dkim=fail header.d=news.example; spf=pass"}]""");
        Assert.False(DkimGate.DkimPass(fail, "list@news.example"));
        Assert.False(DkimGate.DkimPass([], "list@news.example"));
        var refused = await OneClickUnsubscribe.PostAsync(new GuardedHttp(), "http://insecure.example/u", TestContext.Current.CancellationToken);
        Assert.False(refused.IsOk);
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::names")]
    public void Names()
    {
        Assert.Equal("hello", ReaderNames.StripSubjectPrefixes("Re: Fwd: AW: hello"));
        Assert.Equal("x", ReaderNames.StripSubjectPrefixes("RE[2]: x"));
        Assert.Equal("Regarding: x", ReaderNames.StripSubjectPrefixes("Regarding: x"));
        Assert.Equal("Fwd: Lunch", ReaderNames.ForwardSubject("Re: Lunch"));
        Assert.Equal(string.Empty, ReaderNames.ForwardSubject(string.Empty));
        Assert.Equal("a_b_c_ d", ReaderNames.SanitizePart("a/b:c??  d.", 80));
        Assert.Equal("attachments_Q3 report_ final.zip", ReaderNames.BundleName("Q3 report: final"));
        Assert.Equal("attachments.zip", ReaderNames.BundleName("  "));
        var m = J("""{"subject": "Invoice 12/2026", "receivedAt": "2026-11-16T09:05:07Z"}""");
        var n = ReaderNames.EmlExportName(m);
        Assert.True(n.EndsWith("-Invoice 12_2026.eml", StringComparison.Ordinal) && n.StartsWith("2026-11-16 ", StringComparison.Ordinal), n);
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::truncation_and_source_cut")]
    public void Truncation_and_source_cut()
    {
        var m = J("""{"htmlBody": [{"partId": "2"}], "textBody": [{"partId": "1"}], "bodyValues": {"1": {"value": "a", "isTruncated": false}, "2": {"value": "b", "isTruncated": true}}}""");
        Assert.True(SourceCut.DisplayedTruncated(m));
        var ok = J("""{"textBody": [{"partId": "1"}], "bodyValues": {"1": {"value": "a", "isTruncated": false}}, "attachments": [{"partId": "9"}]}""");
        Assert.False(SourceCut.DisplayedTruncated(ok));
        Assert.Equal(("abc", false), SourceCut.Shown("abc"));

        var big = new string('x', SourceCut.DisplayLimit - 1) + "é";
        var (s, cut) = SourceCut.Shown(big);
        Assert.True(cut);
        Assert.Equal(SourceCut.DisplayLimit - 1, Encoding.UTF8.GetByteCount(s));
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::files_tree_and_nodes")]
    public void Files_tree_and_nodes()
    {
        IReadOnlyList<JsonNode> nodes =
        [
            J("""{"id": "a", "name": "Work", "nodeType": "directory", "parentId": null}"""),
            J("""{"id": "b", "name": "2026", "nodeType": "directory", "parentId": "a"}"""),
            J("""{"id": "t", "name": ".Trash", "nodeType": "directory", "parentId": null}"""),
            J("""{"id": "t1", "name": "old", "nodeType": "directory", "parentId": "t"}"""),
            J("""{"id": "f", "name": "x.pdf", "nodeType": "file", "parentId": "a", "blobId": "B"}"""),
            J("""{"id": "c", "@type": "Directory", "name": "archive", "parentId": null}"""),
        ];
        Assert.Equal([new FileFolderRow("c", "archive", 0), new FileFolderRow("a", "Work", 0), new FileFolderRow("b", "2026", 1)], FilesTree.FolderTree(nodes));
        Assert.True(JsonNode.DeepEquals(
            J("""{"@type": "File", "parentId": "a", "name": "x.pdf", "blobId": "B", "mediaType": "application/pdf"}"""),
            FilesTree.FileNode(true, "a", "x.pdf", "B", "application/pdf")));
        Assert.True(JsonNode.DeepEquals(
            J("""{"parentId": null, "name": "x", "blobId": "B", "type": "t"}"""),
            FilesTree.FileNode(false, null, "x", "B", "t")));
    }

    [Fact]
    [Red("reader")]
    [RustTest("reader::message_attachments")]
    public void Message_attachments()
    {
        Assert.True(FilesTree.IsMessageAttachment(J("""{"type": "message/rfc822"}""")));
        Assert.True(FilesTree.IsMessageAttachment(J("""{"type": "application/octet-stream", "name": "Fwd.EML"}""")));
        Assert.False(FilesTree.IsMessageAttachment(J("""{"type": "application/pdf", "name": "a.pdf"}""")));
    }
}
