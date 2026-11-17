// Fable review 3 R3-01: a row of "All inboxes" carries a composite id (login, shared account, server id). Opening it reads body and conversation
// from the login and JMAP account that hold it, with the server id, and the reader keeps speaking in row ids. Two FakeJmap logins with
// different account ids (FakeJmap answers accountNotFound for a wrong one) and the same short email ids. Rust main.rs:426-427 (mail::owner).
// Owner unified.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Cache;
using Barbican.Core.Mail.Model;
using Barbican.Core.Mail.Service;
using Barbican.Core.Mail.Unified;
using Barbican.Core.Plugins.Hooks;
using Barbican.FakeJmap;
using Barbican.Presentation.Common;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Tests.Mail.Loading;
using Barbican.Presentation.Tests.Support;
using Barbican.Presentation.Unified;

namespace Barbican.Presentation.Tests.Unified;

public sealed class UnifiedReadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_row_of_each_login_opens_with_its_own_body_and_conversation()
    {
        var w = await World.StartAsync();
        using var loader = w.Loader("{}");

        var rowA = World.Row("A", w.IdA);
        loader.Open(rowA, OpenReason.Click, ListDirection.None);
        var a = await w.ReadyAsync(rowA.Id);
        Assert.Contains("body of the first login", Text(a.Body!));
        Assert.Equal(rowA.Id, a.Body!.Id);

        var rowB = World.Row("B", w.IdB);
        loader.Open(rowB, OpenReason.Click, ListDirection.None);
        var b = await w.ReadyAsync(rowB.Id);
        Assert.Contains("body of the second login", Text(b.Body!));
        Assert.DoesNotContain("first login", Text(b.Body!));

        // the conversation came from B as well, and its members are B's rows (so opening one routes again)
        Assert.Equal(2, b.Conversation.Count);
        Assert.Contains(b.Conversation, c => string.Equals(c.Id, rowB.Id, StringComparison.Ordinal));
        Assert.All(b.Conversation, c => Assert.Equal("B", UnifiedRows.ParseRow(c.Id)?.Login));
    }

    [Fact]
    public async Task The_body_cache_is_keyed_by_login_and_server_id()
    {
        var w = await World.StartAsync();
        using var loader = w.Loader();
        var rowB = World.Row("B", w.IdB);
        loader.Open(rowB, OpenReason.Click, ListDirection.None);
        await w.ReadyAsync(rowB.Id);

        Assert.True(w.Cache.TryGet("B", w.IdB, out var cached));
        Assert.Contains("second login", Text(cached));
        Assert.False(w.Cache.TryGet("A", rowB.Id, out _));

        // a plain id of the account on screen is still its own, and the same short id of A is not B's
        var plain = new MessageRowViewModel(Mails.Make(w.IdA));
        loader.Open(plain, OpenReason.Click, ListDirection.None);
        var a = await w.ReadyAsync(w.IdA);
        Assert.Contains("first login", Text(a.Body!));
    }

    [Fact]
    public async Task A_row_of_a_login_that_is_not_connected_fails_with_a_reason()
    {
        var w = await World.StartAsync();
        using var loader = w.Loader();
        var row = World.Row("C", "x");
        loader.Open(row, OpenReason.Click, ListDirection.None);
        Assert.True(await SpinUntilAsync(() => w.Reader.BodyState == BodyState.Failed));
        Assert.Contains("not connected", w.Reader.Error ?? string.Empty);
    }

    [Fact]
    public async Task Search_snippets_of_unified_rows_are_asked_of_their_own_login()
    {
        var w = await World.StartAsync();
        var filter = new System.Text.Json.Nodes.JsonObject { ["text"] = "login" };
        var rowA = UnifiedRows.RowId("A", null, w.IdA);
        var rowB = UnifiedRows.RowId("B", null, w.IdB);
        var r = await Barbican.Core.Search.SearchSnippetFetcher.FetchRowsAsync(w.ClientA, l => w.ClientOf(l), filter, [rowA, rowB], Ct);
        Assert.True(r.IsOk, r.Error?.Message);
        Assert.Contains(r.Value!, e => string.Equals(e.EmailId, rowA, StringComparison.Ordinal));
        Assert.Contains(r.Value!, e => string.Equals(e.EmailId, rowB, StringComparison.Ordinal));
    }

    private static string Text(EmailBody body) => string.Concat(body.BodyValues.Values.Select(v => v.Value));

    private static async Task<bool> SpinUntilAsync(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < until)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(5, Ct);
        }

        return condition();
    }

    private sealed class World : IUnifiedLogins
    {
        private readonly Dictionary<string, IJmapClient> _clients = new(StringComparer.Ordinal);

        private World(IJmapClient a, IJmapClient b, string idA, string idB)
        {
            ClientA = a;
            _clients["A"] = a;
            _clients["B"] = b;
            IdA = idA;
            IdB = idB;
        }

        public IJmapClient ClientA { get; }

        public string IdA { get; }

        public string IdB { get; }

        public BodyCache Cache { get; } = new();

        public ReaderViewModel Reader { get; } = new();

        public string? Active => "A";

        public IReadOnlyList<(string Login, IJmapClient? Client)> Logins => [.. _clients.Select(kv => (kv.Key, (IJmapClient?)kv.Value))];

        public static async Task<World> StartAsync()
        {
            var serverA = new FakeJmapServer(new FakeJmapOptions { Origin = "https://a.example.org", AccountId = "a1" });
            var serverB = new FakeJmapServer(new FakeJmapOptions { Origin = "https://b.example.org", AccountId = "b1" });
            var idA = serverA.Store.Deliver(new FakeMail { Subject = "From A", Text = "the body of the first login" });
            var idB = serverB.Store.Deliver(new FakeMail { Subject = "From B", Text = "the body of the second login" });
            serverB.Store.Deliver(new FakeMail { Subject = "Re: From B", Text = "a reply", ReplyTo = idB, ReceivedAt = new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero) });
            var a = await FakeJmapHarness.ConnectAsync(serverA, TimeProvider.System, ct: TestContext.Current.CancellationToken);
            var b = await FakeJmapHarness.ConnectAsync(serverB, TimeProvider.System, ct: TestContext.Current.CancellationToken);
            return new World(a, b, idA, idB);
        }

        public IJmapClient? ClientOf(string login) => _clients.GetValueOrDefault(login);

        public bool IsTestServer(string login) => true;

        public static MessageRowViewModel Row(string login, string id) => new(Mails.Make(UnifiedRows.RowId(login, null, id)));

        public MessageLoader Loader(string prefs = "{\"disableThreading\":true}")
        {
            var active = new FakeActiveMail { AccountKey = "A", Mail = new MailService(ClientA, TimeProvider.System) };
            var fetcher = new MessageFetcher(Cache, new ClientBlobSource(ClientA, ClientA.Session.MailAccountId), new UnifiedReadRoutes(this, TimeProvider.System));
            return new MessageLoader(
                Reader,
                active,
                fetcher,
                new MessagePrefetcher(fetcher),
                new FakeBodyPresenter(),
                new StaticNeighbours(),
                new FakeMailActions(false),
                new HookBus(),
                new InlineDispatcher(),
                TimeProvider.System,
                new TestPrefs(prefs),
                () => new ReaderOptions { Now = Mails.Now, Zone = TimeZoneInfo.Utc });
        }

        public async Task<ReaderSnapshot> ReadyAsync(string rowId)
        {
            for (var i = 0; i < 2000; i++)
            {
                var s = Reader.Current;
                if (string.Equals(s.EmailId, rowId, StringComparison.Ordinal) && s.BodyState is BodyState.Ready or BodyState.Failed)
                {
                    Assert.True(s.BodyState == BodyState.Ready, s.Error);
                    return s;
                }

                await Task.Delay(5, TestContext.Current.CancellationToken);
            }

            throw new TimeoutException("the reader never showed " + rowId + ": " + Reader.Current.BodyState + " " + Reader.Current.Error);
        }
    }
}
