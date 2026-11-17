// M2 plan §6.3 exit "banners for each seed message" (reader part): the invitation is parsed by the server (CalendarEvent/parse on FakeJmap) and
// shown with when, where, organizer and the RSVP note (J-072 partial); the newsletter offers one-click only with the server's DKIM pass; a receipt
// is offered on a writable account only; plain mail gets no banner.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap;
using Barbican.Core.Mail.Mdn;
using Barbican.Core.Mail.Model;
using Barbican.FakeJmap;
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;
using Barbican.Presentation.Reader;

namespace Barbican.Presentation.Tests.Reader;

public sealed class InvitationBannerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_seed_invitation_is_parsed_by_the_server_and_shown()
    {
        var server = new FakeJmapServer(new FakeJmapOptions { SeedM2 = true, CalendarsCapability = true });
        var client = await FakeJmapHarness.ConnectAsync(server, TimeProvider.System, _ => { }, false, Ct);
        var id = server.Store.EmailIds().First(i => string.Equals((string?)server.Store.Email(i)!["subject"], FakeSeed.InvitationSubject, StringComparison.Ordinal));
        var part = server.Store.Email(id)!["attachments"]!.AsArray().First(a => ((string?)a!["type"] ?? string.Empty).StartsWith("text/calendar", StringComparison.Ordinal))!;
        var blob = (string)part["blobId"]!;
        var parsed = await client.SendAsync(
            JmapRequest.Of(JmapRequest.Call("CalendarEvent/parse", new JsonObject { ["accountId"] = client.Session.MailAccountId, ["blobIds"] = new JsonArray(blob) }, "p")),
            Ct);
        var ev = parsed.Value!.Args("p", "CalendarEvent/parse").Value!["parsed"]![blob]![0]!;
        var raw = System.Text.Encoding.UTF8.GetString(server.Store.Blob(blob)!);

        var banner = InvitationBannerSource.Banner(ev, raw, "olga@acme.example", [], ["user@example.org"], TimeZoneInfo.Utc);
        Assert.Equal("invitation", banner.Id);
        Assert.Equal("Invitation: Design review", banner.Title);
        Assert.Contains("Room 4", banner.Detail, StringComparison.Ordinal);
        Assert.Contains("Organizer: Olga", banner.Detail, StringComparison.Ordinal);
        Assert.Equal(InvitationBannerSource.RsvpNote, banner.Note);
        Assert.Empty(banner.Actions);
    }

    [Fact]
    public async Task One_click_needs_the_servers_dkim_pass()
    {
        var body = Newsletter();
        var unsigned = ReaderSnapshots.FromBody(body, null, new ReaderOptions { ServerHost = "example.org" });
        var plain = await ListAsync(new UnsubscribeBannerSource(), unsigned);
        Assert.Equal([UnsubscribeBannerSource.OpenLink], plain.Single().Actions.Select(a => a.ActionId).ToArray());

        var signed = body with
        {
            Headers =
            [
                new EmailHeader("Received", "from out.news.example by mx.example.org with ESMTPS; Tue, 17 Nov 2026 09:00:00 +0000"),
                new EmailHeader("Authentication-Results", "mx.example.org; dkim=pass header.d=news.example; spf=pass smtp.mailfrom=bounce@news.example; dmarc=pass header.from=news.example"),
                .. body.Headers,
            ],
        };
        var snapshot = ReaderSnapshots.FromBody(signed, null, new ReaderOptions { ServerHost = "example.org" });
        Assert.Equal("mx.example.org", snapshot.LearnedAuthServ);
        var oneClick = await ListAsync(new UnsubscribeBannerSource(), snapshot);
        Assert.Equal([UnsubscribeBannerSource.OneClick], oneClick.Single().Actions.Select(a => a.ActionId).ToArray());

        // the user's own mail: no verdict, no banner
        var sent = ReaderSnapshots.FromBody(signed with { MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) { ["m-sent"] = true } }, null, new ReaderOptions
        {
            ServerHost = "example.org",
            MailboxRoles = new Dictionary<string, string>(StringComparer.Ordinal) { ["m-sent"] = "sent" },
        });
        Assert.Equal(AuthVerdict.Unknown, sent.AuthVerdict);
        Assert.Empty(await ListAsync(new UnsubscribeBannerSource(), sent));
    }

    [Fact]
    public async Task A_receipt_is_offered_only_on_a_writable_account()
    {
        var body = new EmailBody
        {
            Id = "e1",
            Subject = "Report",
            From = [new EmailAddress("Boss", "boss@example.org")],
            To = [new EmailAddress(null, "user@example.org")],
            Headers = [new EmailHeader("Disposition-Notification-To", " boss@example.org")],
        };
        var snapshot = ReaderSnapshots.FromBody(body, null, new ReaderOptions());
        var writable = await ListAsync(new ReceiptBannerSource(new Writable(), new MdnSender(TimeProvider.System)), snapshot);
        Assert.Equal([ReceiptBannerSource.Send, ReceiptBannerSource.Ignore], writable.Single().Actions.Select(a => a.ActionId).ToArray());
        Assert.Empty(await ListAsync(new ReceiptBannerSource(new ReadOnlyMailActions(), new MdnSender(TimeProvider.System)), snapshot));
        Assert.Empty(await ListAsync(new ReceiptBannerSource(new Writable(), new MdnSender(TimeProvider.System)), ReaderSnapshots.FromBody(body with { Headers = [] }, null, new ReaderOptions())));
    }

    private static EmailBody Newsletter() => new()
    {
        Id = "n1",
        Subject = "Weekly digest",
        From = [new EmailAddress("News", "digest@news.example")],
        Headers =
        [
            new EmailHeader("List-Unsubscribe", " <https://news.example/unsub?u=4f2a9c>, <mailto:leave@news.example>"),
            new EmailHeader("List-Unsubscribe-Post", " List-Unsubscribe=One-Click"),
        ],
    };

    private static async Task<List<ReaderBanner>> ListAsync(IReaderBannerSource source, ReaderSnapshot snapshot)
    {
        var all = new List<ReaderBanner>();
        await foreach (var b in source.BannersForAsync(snapshot, Ct))
        {
            all.Add(b);
        }

        return all;
    }

    private sealed class Writable : IMailActions
    {
        public bool CanWrite => true;

        public Task<Result<Unit>> MarkReadAsync(string emailId, CancellationToken ct) => Task.FromResult(Result<Unit>.Ok(default));
    }
}
