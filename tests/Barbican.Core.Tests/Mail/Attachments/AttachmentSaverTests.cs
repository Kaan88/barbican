// View source and Download (.eml) read the raw message through the account's own client (Rust reader.rs save_eml :803; Bulwark view-source-dialog.tsx): RawAsync is what both use.
// Spec: I-183, I-288. Owner reader.

using Barbican.Core.Accounts;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Core.Sync;
using Barbican.Core.Tests.Jmap;
using Barbican.FakeJmap;

namespace Barbican.Core.Tests.Mail.Attachments;

public sealed class AttachmentSaverTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<(Fake F, AttachmentSaver Saver, MemoryStream Out, MailTarget Target)> StartAsync()
    {
        var f = await Fake.StartAsync();
        var id = f.Server.Store.Deliver(new FakeMail { Subject = "Quarterly numbers", Text = "Subject: Quarterly numbers\r\n\r\nthe body" });
        var output = new MemoryStream();
        var saver = new AttachmentSaver(new FakeAccounts(f.Client), new NullHookBus(), f.Time, (_, _) => Task.FromResult<Stream?>(new NonClosing(output)));
        var target = new MailTarget("acct", f.Client.Session.MailAccountId, id, string.Empty, id, new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);
        return (f, saver, output, target);
    }

    [Fact]
    public async Task The_raw_message_is_its_blob_named_from_the_subject()
    {
        var (_, saver, _, target) = await StartAsync();
        var raw = await saver.RawAsync(target, Ct);
        Assert.True(raw.IsOk, raw.Error?.Message);
        Assert.Contains("the body", System.Text.Encoding.UTF8.GetString(raw.Value.Bytes), StringComparison.Ordinal);
        Assert.EndsWith(".eml", raw.Value.Name, StringComparison.Ordinal);
        Assert.Contains("Quarterly", raw.Value.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_eml_writes_the_blob_to_the_picked_stream()
    {
        var (_, saver, output, target) = await StartAsync();
        var r = await saver.SaveEmlAsync(target, Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Contains("the body", System.Text.Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_signed_out_account_fails_with_a_message()
    {
        var (_, saver, _, target) = await StartAsync();
        var r = await saver.RawAsync(target with { AccountKey = "gone" }, Ct);
        Assert.False(r.IsOk);
        Assert.Contains("not signed in", r.Error!.Message, StringComparison.Ordinal);
    }

    private sealed class FakeAccounts(IJmapClient client) : IAccountManager
    {
        public IReadOnlyList<AccountInfo> Accounts => [];

        public AccountInfo? Active => null;

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public Task<Barbican.Core.Common.Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Barbican.Core.Common.Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Barbican.Core.Common.Result<Barbican.Core.Common.Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public IAccountSession? SessionFor(string accountKey) => string.Equals(accountKey, "acct", StringComparison.Ordinal) ? new Session(client) : null;
    }

    private sealed class Session(IJmapClient client) : IAccountSession
    {
        public AccountInfo Account => new("acct", "user", "example.org");

        public IJmapClient Client => client;

        public IMailService Mail => throw new NotSupportedException();

        public IMailSync Sync => throw new NotSupportedException();
    }

    // the saver disposes the stream it is given; the test reads the bytes afterwards
    private sealed class NonClosing(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    }
}
