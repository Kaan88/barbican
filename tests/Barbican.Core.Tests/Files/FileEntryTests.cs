// Not Rust rows: B-FILE-001 (both FileNode shapes are read; Rust files.rs:38 is_dir) and the naming rules of Bulwark lib/file-name-rules.ts (G-019). M5 stream files-data.

using System.Text.Json.Nodes;
using Barbican.Core.Files;

namespace Barbican.Core.Tests.Files;

public sealed class FileEntryTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    [Fact]
    [Red("files-data")]
    public void Both_FileNode_shapes_are_read()
    {
        var old = FileEntry.FromJmap(J("""{"id": "f1", "parentId": "d1", "name": "a.txt", "type": "text/plain", "blobId": "b1", "size": 5, "nodeType": "file", "modified": "2026-01-02T03:04:05Z"}"""), "acct", "A1", false).Value!;
        Assert.False(old.IsDirectory);
        Assert.Equal("text/plain", old.MediaType);
        Assert.Equal("d1", old.ParentKey);
        Assert.Equal(5, old.Size);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), old.Modified);

        var draft = FileEntry.FromJmap(J("""{"id": "f2", "parentId": null, "name": "pic.png", "mediaType": "image/png", "blobId": "b2", "@type": "File"}"""), "acct", "A1", false).Value!;
        Assert.False(draft.IsDirectory);
        Assert.Equal("image/png", draft.MediaType);
        Assert.Null(draft.ParentKey);

        Assert.True(FileEntry.FromJmap(J("""{"id": "d", "name": "Docs", "@type": "Directory"}"""), "acct", "A1", false).Value!.IsDirectory);
        Assert.True(FileEntry.FromJmap(J("""{"id": "d", "name": "Docs", "blobId": null}"""), "acct", "A1", false).Value!.IsDirectory);
        Assert.False(FileEntry.FromJmap(J("""{"name": "no id"}"""), "acct", "A1", false).IsOk);

        // a node of a shared account: key and parent key carry the account (B-FILE-003)
        var shared = FileEntry.FromJmap(J("""{"id": "n1", "parentId": "n0", "name": "s.txt", "blobId": "b"}"""), "acct", "A9", true).Value!;
        Assert.Equal("A9|n1", shared.Key);
        Assert.Equal("A9|n0", shared.ParentKey);
        Assert.True(shared.Shared);
    }

    [Fact]
    [Red("files-data")]
    public void Names_are_checked_against_the_server_rules_before_an_upload()
    {
        var rules = FileNameChecks.RulesFrom(J("""{"forbiddenNameChars": "/\\:", "forbiddenNodeNames": ["con", "Aux"], "maxSizeFileNodeName": 10}"""))!;
        Assert.Null(FileNameChecks.RulesFrom(J("""{}""")));
        Assert.Null(FileNameChecks.ProblemOf("anything", null));
        Assert.Null(FileNameChecks.ProblemOf("report.pdf", rules));
        Assert.Equal("tooLong", FileNameChecks.ProblemOf("a-very-long-name.txt", rules)!.Kind);
        Assert.Equal("chars", FileNameChecks.ProblemOf("a:b", rules)!.Kind);
        Assert.Equal("control", FileNameChecks.ProblemOf("a\u0007b", rules)!.Kind);
        Assert.Equal("reserved", FileNameChecks.ProblemOf("CON", rules)!.Kind);
        Assert.Equal("reserved", FileNameChecks.ProblemOf("con.txt", rules)!.Kind);
        Assert.Equal("reserved", FileNameChecks.ProblemOf("Aux.tar.gz", rules)!.Kind);
        Assert.Null(FileNameChecks.ProblemOf("console.txt", rules));
        // the length is in UTF-8 bytes: five two-byte letters are ten bytes, six are twelve
        Assert.Null(FileNameChecks.ProblemOf("ééééé", rules));
        Assert.Equal("tooLong", FileNameChecks.ProblemOf("éééééé", rules)!.Kind);
    }
}
