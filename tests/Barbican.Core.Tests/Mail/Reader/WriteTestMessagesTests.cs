// M2 plan §6.3 step 1 (R2-14): the write-test messages. find returns only messages that carry this run's token in the subject and in the
// X-Barbican-Write-Test header; destroy refuses the whole list when one id lacks the token. The import goes through Email/import, which
// FakeJmap answers once actions' arm is merged (the integrator adds the import round trip then; see the Fable section of reader).

using System.Text;
using Barbican.Core.Mail.Reader;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Reader;

public sealed class WriteTestMessagesTests
{
    private const string Token = "run4f2a9c01";

    [Fact]
    public void The_message_is_synthetic_and_marked()
    {
        var text = Encoding.ASCII.GetString(WriteTestMessages.Build(Token, new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero)));
        Assert.Contains("Subject: Barbican write test [rx:run4f2a9c01]\r\n", text, StringComparison.Ordinal);
        Assert.Contains("X-Barbican-Write-Test: run4f2a9c01\r\n", text, StringComparison.Ordinal);
        Assert.Contains("Date: Tue, 17 Nov 2026 09:00:00 +0000\r\n", text, StringComparison.Ordinal);
        Assert.Contains("From: Barbican write test <rx@example.org>\r\n", text, StringComparison.Ordinal);
        Assert.False(WriteTestMessages.ValidToken("short"));
        Assert.False(WriteTestMessages.ValidToken("has space 12345"));
        Assert.False(WriteTestMessages.ValidToken("rx:injected123"));
        Assert.True(WriteTestMessages.ValidToken(Token));
        Assert.Throws<ArgumentException>(() => WriteTestMessages.Build("bad\r\nBcc: x", DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task Find_returns_only_marked_messages_and_destroy_refuses_foreign_ids()
    {
        var f = await Fake.StartAsync();
        var account = f.Client.Session.MailAccountId!;
        var marked = f.Server.Store.Deliver(new FakeMail { Subject = WriteTestMessages.Subject(Token), Headers = [(WriteTestMessages.Header, Token)] });
        var subjectOnly = f.Server.Store.Deliver(new FakeMail { Subject = WriteTestMessages.Subject(Token) });
        var otherRun = f.Server.Store.Deliver(new FakeMail { Subject = WriteTestMessages.Subject("otherrun99"), Headers = [(WriteTestMessages.Header, "otherrun99")] });
        var plain = f.Server.Store.Deliver(new FakeMail { Subject = "rx:" + Token + " lookalike", Headers = [(WriteTestMessages.Header, Token)] });

        var found = await WriteTestMessages.FindAsync(f.Client, account, Token, TestContext.Current.CancellationToken);
        Assert.True(found.IsOk, found.Error?.Message);
        Assert.Equal([marked], found.Value);

        foreach (var foreign in new[] { subjectOnly, otherRun, plain })
        {
            var refused = await WriteTestMessages.DestroyAsync(f.Client, account, Token, [marked, foreign], TestContext.Current.CancellationToken);
            Assert.False(refused.IsOk);
            Assert.StartsWith("refused:", refused.Error!.Message, StringComparison.Ordinal);
            Assert.Contains(foreign, refused.Error!.Message, StringComparison.Ordinal);
        }

        var stillThere = await WriteTestMessages.FindAsync(f.Client, account, Token, TestContext.Current.CancellationToken);
        Assert.Equal([marked], stillThere.Value);

        var destroyed = await WriteTestMessages.DestroyAsync(f.Client, account, Token, [marked], TestContext.Current.CancellationToken);
        Assert.True(destroyed.IsOk, destroyed.Error?.Message);
        Assert.Equal(1, destroyed.Value);
        Assert.Empty((await WriteTestMessages.FindAsync(f.Client, account, Token, TestContext.Current.CancellationToken)).Value!);
    }

    [Fact]
    public async Task A_bad_token_is_refused_before_any_call()
    {
        var f = await Fake.StartAsync();
        var before = f.Server.ApiCalls.Count;
        Assert.False((await WriteTestMessages.FindAsync(f.Client, "b", "x", TestContext.Current.CancellationToken)).IsOk);
        Assert.False((await WriteTestMessages.DestroyAsync(f.Client, "b", "bad token!", ["m1"], TestContext.Current.CancellationToken)).IsOk);
        Assert.Equal(before, f.Server.ApiCalls.Count);
    }
}
