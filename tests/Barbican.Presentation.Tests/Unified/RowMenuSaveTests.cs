// Fable review 3 R3-02: "Download (.eml)" and "View source" of the message row menu run with the target the row menu builds
// (UnifiedTargetResolver, always registered; ContextPointerRouter's fallback without a resolver). The MailTarget contract: AccountKey the
// login on screen, AccountId the JMAP account or empty for the login's own, EmailId the list id; code that talks to a server asks
// Login, JmapAccountId and ServerId. Two FakeJmap logins with different account ids (FakeJmap answers accountNotFound for a wrong one).
// Spec: I-183, I-288. Owner unified.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using System.Text;
using Barbican.Core.Accounts;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail;
using Barbican.Core.Mail.Attachments;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Plugins.Hooks;
using Barbican.FakeJmap;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Tests.Support;
using Barbican.Presentation.Unified;

namespace Barbican.Presentation.Tests.Unified;

public sealed class RowMenuSaveTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task View_source_and_download_of_a_plain_row_read_the_account_on_screen()
    {
        var w = await World.StartAsync();
        var target = w.Resolver.Resolve(new MessageRowViewModel(Mails.Make(w.IdA)))!;

        var raw = await w.Saver.RawAsync(target, Ct);
        Assert.True(raw.IsOk, raw.Error?.Message);
        Assert.Contains("first login", Encoding.UTF8.GetString(raw.Value.Bytes));

        var saved = await w.Saver.SaveEmlAsync(target, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        Assert.Contains("first login", Encoding.UTF8.GetString(w.Written.ToArray()));
    }

    [Fact]
    public async Task View_source_and_download_of_a_unified_row_of_another_login_read_that_login()
    {
        var w = await World.StartAsync();
        var target = w.Resolver.Resolve(new MessageRowViewModel(Mails.Make(UnifiedRows.RowId("B", null, w.IdB))))!;
        Assert.Equal("B", target.Login);
        Assert.Equal(w.IdB, target.ServerId);

        var raw = await w.Saver.RawAsync(target, Ct);
        Assert.True(raw.IsOk, raw.Error?.Message);
        Assert.Contains("second login", Encoding.UTF8.GetString(raw.Value.Bytes));

        var saved = await w.Saver.SaveEmlAsync(target, Ct);
        Assert.True(saved.IsOk, saved.Error?.Message);
        Assert.Contains("second login", Encoding.UTF8.GetString(w.Written.ToArray()));
    }

    [Fact]
    public async Task A_unified_row_of_the_login_on_screen_reads_its_server_id()
    {
        var w = await World.StartAsync();
        var target = w.Resolver.Resolve(new MessageRowViewModel(Mails.Make(UnifiedRows.RowId("A", null, w.IdA))))!;
        var raw = await w.Saver.RawAsync(target, Ct);
        Assert.True(raw.IsOk, raw.Error?.Message);
        Assert.Contains("first login", Encoding.UTF8.GetString(raw.Value.Bytes));
    }

    [Fact]
    public async Task The_fallback_target_without_a_resolver_reads_the_logins_own_account()
    {
        // ContextPointerRouter.Target without IMailTargetResolver: the account key and an empty account id
        var w = await World.StartAsync();
        var target = new MailTarget("A", string.Empty, w.IdA, string.Empty, w.IdA, new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None);
        var raw = await w.Saver.RawAsync(target, Ct);
        Assert.True(raw.IsOk, raw.Error?.Message);
    }

    [Fact]
    public async Task The_reader_menu_target_of_an_open_unified_row_reads_that_login()
    {
        // ReaderMenuOpener and rx-save build MailTarget.OfOpen from the open body, whose id is the row id
        var w = await World.StartAsync();
        var body = new EmailBody { Id = UnifiedRows.RowId("B", null, w.IdB), ThreadId = UnifiedRows.RowId("B", null, "t") };
        var raw = await w.Saver.RawAsync(MailTarget.OfOpen("A", body), Ct);
        Assert.True(raw.IsOk, raw.Error?.Message);
        Assert.Contains("second login", Encoding.UTF8.GetString(raw.Value.Bytes));
    }

    [Fact]
    public void A_shared_row_names_the_shared_account()
    {
        var target = new UnifiedTargetResolver(new FakeActiveMail { AccountKey = "A" }).Resolve(new MessageRowViewModel(Mails.Make(UnifiedRows.RowId("B", "g7", "m1"))))!;
        Assert.Equal("B", target.Login);
        Assert.Equal("g7", target.JmapAccountId("own"));
        Assert.Equal("m1", target.ServerId);
        Assert.Equal("own", new MailTarget("A", string.Empty, "m1", string.Empty, "m1", new Dictionary<string, bool>(StringComparer.Ordinal), Keywords.None).JmapAccountId("own"));
    }

    private sealed class World : IAccountManager
    {
        private readonly Dictionary<string, IJmapClient> _clients = new(StringComparer.Ordinal);

        private World(IJmapClient a, IJmapClient b, string idA, string idB)
        {
            _clients["A"] = a;
            _clients["B"] = b;
            IdA = idA;
            IdB = idB;
            Resolver = new UnifiedTargetResolver(new FakeActiveMail { AccountKey = "A" });
            Saver = new AttachmentSaver(this, new HookBus(), TimeProvider.System, (_, _) => Task.FromResult<Stream?>(new NonClosing(Written)));
        }

        public event EventHandler<ActiveAccountChangedEventArgs>? ActiveChanged
        {
            add { }
            remove { }
        }

        public string IdA { get; }

        public string IdB { get; }

        public UnifiedTargetResolver Resolver { get; }

        public AttachmentSaver Saver { get; }

        public MemoryStream Written { get; } = new();

        public IReadOnlyList<AccountInfo> Accounts => [];

        public AccountInfo? Active => null;

        public static async Task<World> StartAsync()
        {
            var serverA = new FakeJmapServer(new FakeJmapOptions { Origin = "https://a.example.org", AccountId = "a1" });
            var serverB = new FakeJmapServer(new FakeJmapOptions { Origin = "https://b.example.org", AccountId = "b1" });
            var idA = serverA.Store.Deliver(new FakeMail { Subject = "From A", Text = "the first login" });
            var idB = serverB.Store.Deliver(new FakeMail { Subject = "From B", Text = "the second login" });
            var a = await FakeJmapHarness.ConnectAsync(serverA, TimeProvider.System, ct: TestContext.Current.CancellationToken);
            var b = await FakeJmapHarness.ConnectAsync(serverB, TimeProvider.System, ct: TestContext.Current.CancellationToken);
            return new World(a, b, idA, idB);
        }

        public Task<Result<AccountInfo>> SignInWithPasswordAsync(PasswordSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<AccountInfo>> SignInWithOAuthAsync(OAuthSignIn request, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<Unit>> SwitchToAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

        public IAccountSession? SessionFor(string accountKey) => _clients.TryGetValue(accountKey, out var c) ? new Session(accountKey, c) : null;
    }

    private sealed class Session(string key, IJmapClient client) : IAccountSession
    {
        public AccountInfo Account => new(key, "user", "example.org");

        public IJmapClient Client => client;

        public IMailService Mail => throw new NotSupportedException();

        public IMailSync Sync => throw new NotSupportedException();
    }

    // the saver disposes the stream it is given; the test reads the bytes afterwards
    private sealed class NonClosing(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);
    }
}
