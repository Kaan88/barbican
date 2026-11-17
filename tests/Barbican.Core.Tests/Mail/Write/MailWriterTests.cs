// MailWriter against the fake server (M2 plan 6.1 exit tests): chunking at a maxObjectsInSet of 2, set errors mapped to user text,
// a read-only account refused locally with zero requests, and each method's effect on the store.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Mail.Write;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Write;

public sealed class MailWriterTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Fake F, MailWriter W)> StartAsync(int maxObjects = 500, bool readOnly = false)
    {
        var f = await Fake.StartAsync(new FakeJmapOptions { MaxObjectsInSet = maxObjects }, readOnly: readOnly);
        return (f, new MailWriter(f.Client, f.Server.Options.AccountId));
    }

    private static int Calls(Fake f, string method) => f.Server.ApiCalls.Count(c => c.Contains(method, StringComparison.Ordinal));

    [Fact]
    public async Task Keywords_are_chunked_by_max_objects_in_set()
    {
        var (f, w) = await StartAsync(maxObjects: 2);
        var ids = f.Server.Store.EmailIds().Take(5).ToList();
        var r = await w.UpdateKeywordsAsync(ids, new Dictionary<string, bool>(StringComparer.Ordinal) { ["$flagged"] = true, ["$seen"] = false }, Ct);
        Assert.True(r.IsOk);
        Assert.Equal(5, r.Value!.Updated.Count);
        Assert.Equal(3, Calls(f, "Email/set"));
        Assert.All(ids, id => Assert.NotNull(f.Server.Store.Email(id)!["keywords"]!["$flagged"]));
    }

    [Fact]
    public async Task A_refused_update_is_mapped_to_user_text()
    {
        var (_, w) = await StartAsync();
        var r = await w.UpdateKeywordsAsync(["nope"], new Dictionary<string, bool>(StringComparer.Ordinal) { ["$flagged"] = true }, Ct);
        Assert.True(r.IsOk);
        Assert.False(r.Value!.AllAccepted);
        Assert.Contains("It no longer exists.", r.Value.Refused[0].Message, StringComparison.Ordinal);
        Assert.Empty(r.Value.Updated);
    }

    [Fact]
    public async Task An_invalid_keyword_is_refused_locally()
    {
        var (f, w) = await StartAsync();
        var before = f.Server.ApiCalls.Count;
        var r = await w.UpdateKeywordsAsync(["x"], new Dictionary<string, bool>(StringComparer.Ordinal) { ["bad keyword"] = true }, Ct);
        Assert.False(r.IsOk);
        Assert.IsType<ValidationError>(r.Error);
        Assert.Equal(before, f.Server.ApiCalls.Count);
    }

    [Fact]
    public async Task A_read_only_account_is_refused_with_zero_requests()
    {
        var (f, w) = await StartAsync(readOnly: true);
        var before = f.Server.ApiCalls.Count;
        var id = f.Server.Store.EmailIds()[0];
        var inbox = f.Server.Store.MailboxId("inbox")!;
        List<Result<SetReport>> results =
        [
            await w.UpdateKeywordsAsync([id], new Dictionary<string, bool>(StringComparer.Ordinal) { ["$flagged"] = true }, Ct),
            await w.MoveAsync([id], null, inbox, Ct),
            await w.DestroyAsync([id], Ct),
            await w.UpdateMailboxesAsync(new Dictionary<string, MailboxPatch>(StringComparer.Ordinal) { [inbox] = new() { Name = "x" } }, Ct),
        ];
        Assert.All(results, r =>
        {
            Assert.False(r.IsOk);
            Assert.IsType<ReadOnlyViolation>(r.Error);
            Assert.Equal("This account is read-only.", r.Error!.Message);
        });
        Assert.False((await w.EmptyMailboxAsync(inbox, Ct)).IsOk);
        Assert.False((await w.MarkMailboxReadAsync(inbox, true, Ct)).IsOk);
        Assert.False((await w.CreateMailboxAsync("X", null, null, Ct)).IsOk);
        Assert.False((await w.DestroyMailboxAsync(inbox, false, Ct)).IsOk);
        Assert.False((await w.UploadAsync(new byte[] { 1 }, "text/plain", Ct)).IsOk);
        Assert.Equal(before, f.Server.ApiCalls.Count);
        Assert.NotNull(f.Server.Store.Email(id));
    }

    [Fact]
    public async Task Move_replaces_the_folder_set_or_swaps_one_folder()
    {
        var (f, w) = await StartAsync();
        var store = f.Server.Store;
        var archive = store.MailboxId("archive")!;
        var inbox = store.MailboxId("inbox")!;
        var id = store.EmailIds().First(i => store.Email(i)!["mailboxIds"]![inbox] is not null);
        Assert.True((await w.MoveAsync([id], null, archive, Ct)).IsOk);
        var boxes = (JsonObject)store.Email(id)!["mailboxIds"]!;
        Assert.True(boxes.Count == 1 && boxes.ContainsKey(archive));

        // a message in two folders: moving from one keeps the other
        store.Patch(id, e => { ((JsonObject)e["mailboxIds"]!)[inbox] = true; return true; });
        var junk = store.MailboxId("junk")!;
        Assert.True((await w.MoveAsync([id], inbox, junk, Ct)).IsOk);
        boxes = (JsonObject)store.Email(id)!["mailboxIds"]!;
        Assert.Equal(new[] { archive, junk }.Order(StringComparer.Ordinal), boxes.Select(kv => kv.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Destroy_removes_messages_in_chunks()
    {
        var (f, w) = await StartAsync(maxObjects: 2);
        var ids = f.Server.Store.EmailIds().Take(3).ToList();
        var r = await w.DestroyAsync(ids, Ct);
        Assert.True(r.IsOk);
        Assert.Equal(3, r.Value!.Destroyed.Count);
        Assert.Equal(2, Calls(f, "Email/set"));
        Assert.All(ids, id => Assert.Null(f.Server.Store.Email(id)));
    }

    [Fact]
    public async Task Empty_mailbox_pages_until_the_folder_is_empty()
    {
        var (f, w) = await StartAsync(maxObjects: 2);
        var trash = f.Server.Store.MailboxId("trash")!;
        for (var i = 0; i < 5; i++)
        {
            f.Server.Store.Deliver(new FakeMail { Subject = "t" + i, Text = "x" }, trash);
        }

        var r = await w.EmptyMailboxAsync(trash, Ct);
        Assert.True(r.IsOk);
        Assert.Equal(5, r.Value);
        Assert.Empty(f.Server.Store.Query(new JsonObject { ["inMailbox"] = trash }));
    }

    [Fact]
    public async Task Mark_mailbox_read_includes_subfolders_on_request()
    {
        var (f, w) = await StartAsync();
        var store = f.Server.Store;
        var inbox = store.MailboxId("inbox")!;
        var child = store.Mailboxes().First(m => string.Equals((string?)m["parentId"], inbox, StringComparison.Ordinal))["id"]!.GetValue<string>();
        store.Deliver(new FakeMail { Subject = "child mail", Text = "x" }, child);
        Assert.True((await w.MarkMailboxReadAsync(inbox, false, Ct)).IsOk);
        Assert.Single(store.Query(new JsonObject { ["notKeyword"] = "$seen", ["inMailbox"] = child }));
        var all = await w.MarkMailboxReadAsync(inbox, true, Ct);
        Assert.Equal(1, all.Value);
        Assert.Empty(store.Query(new JsonObject { ["notKeyword"] = "$seen", ["inMailbox"] = child }));
    }

    [Fact]
    public async Task Mailbox_create_rename_reparent_and_destroy()
    {
        var (f, w) = await StartAsync();
        var store = f.Server.Store;
        var created = await w.CreateMailboxAsync("Receipts", null, null, Ct);
        Assert.True(created.IsOk);
        var id = created.Value!.Id;
        var dup = await w.CreateMailboxAsync("receipts", null, null, Ct);
        Assert.False(dup.IsOk);
        Assert.Contains("already exists", dup.Error!.Message, StringComparison.Ordinal);

        var inbox = store.MailboxId("inbox")!;
        var up = await w.UpdateMailboxesAsync(new Dictionary<string, MailboxPatch>(StringComparer.Ordinal) { [id] = new() { Name = "Bills", SetParent = true, ParentId = inbox, SortOrder = 7 } }, Ct);
        Assert.True(up.IsOk && up.Value!.AllAccepted);
        var row = store.Mailboxes().First(m => string.Equals((string?)m["id"], id, StringComparison.Ordinal));
        Assert.Equal("Bills", (string?)row["name"]);
        Assert.Equal(inbox, (string?)row["parentId"]);

        // a folder can't go under its own child
        var child = (await w.CreateMailboxAsync("Deep", id, null, Ct)).Value!.Id;
        var cycle = await w.UpdateMailboxesAsync(new Dictionary<string, MailboxPatch>(StringComparer.Ordinal) { [id] = new() { SetParent = true, ParentId = child } }, Ct);
        Assert.False(cycle.Value!.AllAccepted);

        // a folder with a subfolder or messages is refused, then goes with removeEmails
        var hasChild = await w.DestroyMailboxAsync(id, false, Ct);
        Assert.Contains("subfolders", hasChild.Error!.Message, StringComparison.Ordinal);
        store.Deliver(new FakeMail { Subject = "in deep", Text = "x" }, child);
        var notEmpty = await w.DestroyMailboxAsync(child, false, Ct);
        Assert.Contains("not empty", notEmpty.Error!.Message, StringComparison.Ordinal);
        Assert.True((await w.DestroyMailboxAsync(child, true, Ct)).IsOk);
        Assert.True((await w.DestroyMailboxAsync(id, false, Ct)).IsOk);
        Assert.False(store.HasMailbox(id));
    }

    [Fact]
    public async Task Upload_then_import_in_chunks_reports_created_by_index()
    {
        var (f, w) = await StartAsync(maxObjects: 2);
        var inbox = f.Server.Store.MailboxId("inbox")!;
        List<EmailImportEntry> entries = [];
        for (var i = 0; i < 3; i++)
        {
            var up = await w.UploadAsync(Encoding.UTF8.GetBytes($"Subject: import {i}\r\n\r\nbody {i}"), "message/rfc822", Ct);
            Assert.True(up.IsOk, up.Error?.Message);
            entries.Add(new EmailImportEntry(up.Value!.BlobId, [inbox], ["$seen"], null));
        }

        entries.Add(new EmailImportEntry("missing", [inbox], [], null));
        var r = await w.ImportAsync(entries, Ct);
        Assert.True(r.IsOk);
        Assert.Equal([0, 1, 2], r.Value!.Created.Keys.Order());
        Assert.Single(r.Value.Refused);
        Assert.Equal(2, Calls(f, "Email/import"));
    }

    [Fact]
    public async Task Copy_creates_messages_and_reports_failures()
    {
        var (f, w) = await StartAsync();
        var store = f.Server.Store;
        var archive = store.MailboxId("archive")!;
        var id = store.EmailIds()[0];
        var r = await w.CopyToAccountAsync("other", [new EmailCopyEntry(id, [archive], ["$seen"]), new EmailCopyEntry("gone", [archive], [])], Ct);
        Assert.True(r.IsOk);
        Assert.Single(r.Value!.Created);
        Assert.Single(r.Value.Refused);
        Assert.NotNull(store.Email(r.Value.Created[0]));
    }

    [Fact]
    public async Task Import_keeps_the_Message_ID_of_the_uploaded_blob()
    {
        var (f, w) = await StartAsync();
        var inbox = f.Server.Store.MailboxId("inbox")!;
        var up = await w.UploadAsync(Encoding.UTF8.GetBytes("Message-ID: <kept-1@example.org>\r\nSubject: keep\r\n\r\nbody"), "message/rfc822", Ct);
        var r = await w.ImportAsync([new EmailImportEntry(up.Value!.BlobId, [inbox], [], null)], Ct);
        Assert.True(r.IsOk);
        var email = f.Server.Store.Email(r.Value!.Created[0])!;
        Assert.Equal("kept-1@example.org", (string?)((System.Text.Json.Nodes.JsonArray)email["messageId"]!)[0]);
    }
}
