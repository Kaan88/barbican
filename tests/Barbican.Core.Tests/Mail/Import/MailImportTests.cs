// Ported from Rust src/mailimport.rs tests (:395-:528).
// The Rust Mock server becomes an IImportServer fake with the same behaviour (maxObjectsInSet 2, maxSizeUpload 1000, one entry refused when fail_one).

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Mail.Import;

namespace Barbican.Core.Tests.Mail.Import;

public sealed class MailImportTests
{
    private static readonly byte[] Mbox = Encoding.ASCII.GetBytes(
        "From a@b.c Tue May  5 10:11:12 2020\nSubject: one\nStatus: RO\nX-Status: F\nDate: Tue, 05 May 2020 10:11:12 +0200\n\nHello\n>From the start\nFrom now on, unquoted\n\n"
        + "From c@d.e Wed May  6 08:00:00 2020\r\nSubject: two\r\nX-Mozilla-Status: 0008\r\n\r\nGone\r\n\r\n"
        + "From x@y.z Thu May  7 09:00 +0000 2020\nSubject: three\n\nBody\n");

    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    [Fact]
    [RustTest("mailimport::summaries")]
    public void Summaries()
    {
        Assert.Equal("Imported 1 message", MboxFormat.Summary(1, 0, 0, null));
        Assert.Equal("Imported 2 messages, 3 already in the folder", MboxFormat.Summary(2, 3, 0, null));
        Assert.Equal("Imported 3 messages, 1 not imported: The message is invalid.", MboxFormat.Summary(3, 0, 1, "The message is invalid."));
    }

    [Fact]
    [RustTest("mailimport::splits_mbox")]
    public void Splits_mbox()
    {
        var m = MboxFormat.Split(Mbox);
        Assert.Equal(3, m.Count);
        var one = Encoding.UTF8.GetString(m[0]);
        Assert.StartsWith("Subject: one\n", one, StringComparison.Ordinal);

        // one ">" comes off, a body "From " after a line of text is not a separator, the empty line before the next separator is dropped
        Assert.EndsWith("Hello\nFrom the start\nFrom now on, unquoted\n", one, StringComparison.Ordinal);
        Assert.EndsWith("Gone\r\n", Encoding.UTF8.GetString(m[1]), StringComparison.Ordinal);
        Assert.Contains("Subject: three", Encoding.UTF8.GetString(m[2]), StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("mailimport::envelope_lines")]
    public void Envelope_lines()
    {
        Assert.True(MboxFormat.IsEnvelopeLine("From a@b.c Tue May  5 10:11:12 2020\n"u8));
        Assert.True(MboxFormat.IsEnvelopeLine("From MAILER-DAEMON Thu May  7 09:00 +0000 2020"u8));
        Assert.True(MboxFormat.IsEnvelopeLine("From a Tue May  5 10:11:12 2020 remote from host"u8));
        Assert.False(MboxFormat.IsEnvelopeLine("From now on, we meet at 10:00 every day\n"u8));
        Assert.False(MboxFormat.IsEnvelopeLine("Subject: x"u8));
    }

    [Fact]
    [RustTest("mailimport::states_and_dates")]
    public void States_and_dates()
    {
        var items = MboxFormat.ItemsOf("box.mbox", Mbox);

        // the expunged one is left out
        Assert.Equal(2, items.Count);
        Assert.Equal(["$seen", "$flagged"], items[0].Keywords);
        Assert.Equal("2020-05-05T08:11:12Z", items[0].ReceivedAt);

        // no status headers: read
        Assert.Equal(["$seen"], items[1].Keywords);
        var f = MboxFormat.HeaderFields("X-Mozilla-Status: 0001\nX-Keywords: Work, \\Flagged $Junk\n\nbody"u8);
        var (kw, gone) = MboxFormat.State(f);
        Assert.False(gone);
        Assert.Equal(["$seen", "work", "$flagged", "$junk"], kw);
        var unread = MboxFormat.HeaderFields("Status: O\nDate: Mon, 16 Nov 2026 17:00:00 +0000 (UTC)\n\n"u8);
        Assert.Empty(MboxFormat.State(unread).Keywords);
        Assert.Equal("2026-11-16T17:00:00Z", MboxFormat.ReceivedAt(unread));
    }

    [Fact]
    [RustTest("mailimport::eml_is_one_message_and_entries")]
    public void Eml_is_one_message_and_entries()
    {
        var eml = "Subject: hi\r\nFrom: a@b.c\r\n\r\nFrom here on it is body\r\n"u8.ToArray();
        var items = MboxFormat.ItemsOf("note.eml", eml);
        Assert.Single(items);
        Assert.Equal(eml, items[0].Bytes);
        var e = MboxFormat.ImportEntry("B1", "M1", items[0]);
        Assert.Equal("B1", (string)e["blobId"]!);
        Assert.True((bool)e["mailboxIds"]!["M1"]!);
        Assert.True((bool)e["keywords"]!["$seen"]!);
        Assert.False(e.ContainsKey("receivedAt"));

        // folded headers are unfolded
        var f = MboxFormat.HeaderFields("Subject: a\n  b\n\n"u8);
        Assert.Equal(("subject", "a b"), f[0]);
    }

    [Fact]
    [RustTest("mailimport::import_loop_created_not_created_and_duplicates")]
    public async Task Import_loop_created_not_created_and_duplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        var m = new MockServer(["b@x"], failOne: true);
        var big = new ImportItem(new byte[2000], [], null);

        // b@x is in the folder already; the second a@x repeats one in the same file
        using var items = new List<ImportItem> { Msg("a@x"), Msg("b@x"), Msg("c@x"), big, Msg("a@x") }.GetEnumerator();
        var seen = new List<int>();
        var o = await MailImportLoop.ImportAsync(m, "M1", items, seen.Add, ct);

        // b@x is already in the folder, the big one is refused before uploading
        Assert.Equal(2, o.Already);
        Assert.Equal(2, m.Uploads);

        // batch 1: a imported; batch 2: c imported (first entry), nothing refused
        Assert.Equal(2, o.Imported);
        Assert.Equal(["A message is larger than the server takes."], o.Errors);
        Assert.Equal([2, 4, 5], seen);
        Assert.Equal(2, m.Calls.Count);
        var imp = (JsonObject)m.Calls[0][0]![1]!["emails"]!;
        Assert.Single(imp);
        Assert.Equal("B1", (string)imp["m0"]!["blobId"]!);

        // a notCreated answer is reported
        var m2 = new MockServer([], failOne: true);
        using var two = new List<ImportItem> { Msg("a@x"), Msg("b@x") }.GetEnumerator();
        var o2 = await MailImportLoop.ImportAsync(m2, "M1", two, _ => { }, ct);
        Assert.Equal(1, o2.Imported);
        Assert.Single(o2.Errors);
    }

    [Fact]
    [RustTest("mailimport::streaming_reader_and_eml_cap")]
    public void Streaming_reader_and_eml_cap()
    {
        var r = new MboxReader("box.mbox", new BufferedStream(new MemoryStream(Mbox), 16), long.MaxValue);
        var a = r.NextItem()!;
        Assert.StartsWith("Subject: one", Encoding.UTF8.GetString(a.Bytes), StringComparison.Ordinal);
        var b = r.NextItem()!;
        Assert.Contains("Subject: three", Encoding.UTF8.GetString(b.Bytes), StringComparison.Ordinal);
        Assert.Null(r.NextItem());
        Assert.Equal(Mbox.Length, r.BytesRead);

        var big = new MboxReader("x.eml", new MemoryStream("Subject: x\r\n\r\n0123456789"u8.ToArray()), 10);
        Assert.Null(big.NextItem());
        Assert.True(big.TooBig);
        Assert.Equal("a.b@c", MboxFormat.MessageId("Message-Id: <a.b@c>\r\n\r\n"u8));
        Assert.Null(MboxFormat.MessageId("Subject: x\r\n\r\n"u8));
    }

    private sealed class MockServer(IReadOnlyList<string> have, bool failOne) : IImportServer
    {
        public List<JsonNode> Calls { get; } = [];

        public int Uploads { get; private set; }

        public string Account => "A";

        public int MaxSet => 2;

        public int MaxUpload => 1000;

        public Task<Result<string>> UploadAsync(byte[] data, CancellationToken ct)
        {
            Uploads++;
            return Task.FromResult(Result<string>.Ok($"B{Uploads}"));
        }

        public Task<Result<IReadOnlyList<JsonNode>>> CallAsync(JsonNode calls, CancellationToken ct)
        {
            Calls.Add(calls.DeepClone());
            var created = new JsonObject();
            var not = new JsonObject();
            foreach (var (key, _) in (JsonObject)calls[0]![1]!["emails"]!)
            {
                if (failOne && created.Count > 0)
                {
                    not[key] = J("""{"type": "invalidEmail", "description": "bad"}""");
                }
                else
                {
                    created[key] = J("""{"id": "E"}""");
                }
            }

            IReadOnlyList<JsonNode> reply = [new JsonArray("Email/import", new JsonObject { ["created"] = created, ["notCreated"] = not }, "i")];
            return Task.FromResult(Result<IReadOnlyList<JsonNode>>.Ok(reply));
        }

        public Task<IReadOnlySet<string>> MessageIdsInAsync(string mailboxId, CancellationToken ct)
        {
            Assert.Equal("M1", mailboxId);
            return Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(have, StringComparer.Ordinal));
        }
    }

    private static ImportItem Msg(string id) =>
        new(Encoding.ASCII.GetBytes($"Message-ID: <{id}>\r\nSubject: s\r\n\r\nb\r\n"), ["$seen"], null);
}
