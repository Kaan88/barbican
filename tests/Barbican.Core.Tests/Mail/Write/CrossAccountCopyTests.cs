// CrossAccountCopy against the fake server (spec G-048): one login copies with Email/copy (keywords carried, fromAccountId set), two logins
// move the raw blob across with an upload and Email/import and keep the original, and a notCreated refusal comes back as a failed result.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Write;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Write;

public sealed class CrossAccountCopyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MailTarget Target(string accountKey, string emailId) =>
        new(accountKey, string.Empty, emailId, string.Empty, emailId, new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);

    [Fact]
    public async Task One_login_copies_with_Email_copy_and_keeps_the_original()
    {
        var f = await Fake.StartAsync();
        var id = f.Server.Store.EmailIds()[0];
        var before = f.Server.Store.EmailIds().Count;
        var copy = new CrossAccountCopy(_ => (new MailWriter(f.Client, f.Server.Options.AccountId), f.Client));
        var inbox = f.Server.Store.MailboxId("inbox")!;

        var r = await copy.CopyAsync([Target("a", id)], "a", inbox, Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(1, r.Value);
        Assert.NotNull(f.Server.Store.Email(id));
        Assert.Contains("Email/get -> Email/get", f.Trace, StringComparer.Ordinal);
        Assert.Equal("Email/copy -> Email/copy", f.Trace[^1]);
        Assert.Equal(before + 1, f.Server.Store.EmailIds().Count);
    }

    [Fact]
    public async Task Two_logins_upload_and_import_the_blob_and_keep_the_original()
    {
        var source = await Fake.StartAsync();
        var dest = await Fake.StartAsync(new FakeJmapOptions { AccountId = "other" });
        var copy = new CrossAccountCopy(key => key switch
        {
            "a" => (new MailWriter(source.Client, source.Server.Options.AccountId), (IJmapClient)source.Client),
            "c" => (new MailWriter(dest.Client, dest.Server.Options.AccountId), dest.Client),
            _ => null,
        });
        var id = source.Server.Store.EmailIds()[0];
        var inbox = dest.Server.Store.MailboxId("inbox")!;
        var inDest = dest.Server.Store.Query(new JsonObject { ["inMailbox"] = inbox }).Count;

        var r = await copy.CopyAsync([Target("a", id)], "c", inbox, Ct);

        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Equal(1, r.Value);
        Assert.NotNull(source.Server.Store.Email(id));
        Assert.NotNull(dest.Server.LastUploadedBlobId);
        Assert.Equal("Email/import -> Email/import", dest.Trace[^1]);
        var arrived = dest.Server.Store.Query(new JsonObject { ["inMailbox"] = inbox });
        Assert.Equal(inDest + 1, arrived.Count);
    }

    [Fact]
    public async Task A_notCreated_refusal_is_a_failed_result()
    {
        var f = await Fake.StartAsync();
        var id = f.Server.Store.EmailIds()[0];
        var before = f.Server.Store.EmailIds().Count;
        var copy = new CrossAccountCopy(_ => (new MailWriter(f.Client, f.Server.Options.AccountId), f.Client));

        var r = await copy.CopyAsync([Target("a", id)], "a", "missing", Ct);

        Assert.False(r.IsOk);
        Assert.StartsWith("Failed to copy email:", r.Error!.Message, StringComparison.Ordinal);
        Assert.Equal(before, f.Server.Store.EmailIds().Count);
    }
}
