// Fable review 2 R2-07 exit tests (M2 plan §6.3 step 5): an Authentication-Results counts only inside the server's own trace block (below the
// topmost Received whose "by" host is the authserv-id, above the next Received); forged headers above the block and below it are ignored; the
// authserv-id is learned under a trusted host and pinned per account. The header order is the one Stalwart writes (smtp/inbound/data.rs:482).

using Barbican.Core.Mail.AuthResults;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Mail.AuthResults;

public sealed class AuthTraceTests
{
    private const string Server = "mx.example.org";

    private static readonly EmailHeader Received = new("Received", $" from relay.news.example (relay.news.example [192.0.2.4])\r\n\tby {Server} (Stalwart) with ESMTPS id 1A2B;\r\n\tMon, 16 Nov 2026 09:05:07 +0000");
    private static readonly EmailHeader Own = new("Authentication-Results", $" {Server};\r\n\tdkim=pass header.d=news.example;\r\n\tspf=pass smtp.mailfrom=news.example;\r\n\tdmarc=pass header.from=news.example");
    private static readonly EmailHeader Upstream = new("Received", " from sender.news.example by relay.news.example; Mon, 16 Nov 2026 09:05:05 +0000");
    private static readonly EmailHeader ForgedBelow = new("Authentication-Results", $" {Server}; dkim=pass header.d=bank.example; dmarc=pass header.from=bank.example");

    private static bool Trusts(string host) => host.EndsWith("example.org", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void The_server_block_counts_and_its_id_is_learned()
    {
        var r = AuthTrace.ServerResults([Received, Own, Upstream, ForgedBelow], null, Trusts, out var learned);
        Assert.Equal(Server, learned);
        Assert.Contains(r, x => string.Equals(x.Method, "dmarc", StringComparison.Ordinal) && string.Equals(x.Result, "pass", StringComparison.Ordinal));
        Assert.True(DkimGate.ServerDkimPass(r, "list@news.example"));
        Assert.False(DkimGate.ServerDkimPass(r, "ceo@bank.example"));
    }

    [Fact]
    public void A_forged_header_below_the_trace_block_is_ignored()
    {
        // the message carries its own "mx.example.org; dkim=pass" in its header block, under the upstream Received: never counted
        var r = AuthTrace.ServerResults([Received, Upstream, ForgedBelow], null, Trusts, out var learned);
        Assert.Empty(r);
        Assert.Null(learned);
    }

    [Fact]
    public void A_forged_header_above_the_trace_block_is_ignored()
    {
        // a header written above the server's Received cannot come from the server, which writes Received first
        var r = AuthTrace.ServerResults([ForgedBelow, Received, Upstream], null, Trusts, out _);
        Assert.Empty(r);

        // and when both exist, only the one inside the block counts
        var both = AuthTrace.ServerResults([ForgedBelow, Received, Own, Upstream], null, Trusts, out _);
        Assert.False(DkimGate.ServerDkimPass(both, "ceo@bank.example"));
        Assert.True(DkimGate.ServerDkimPass(both, "list@news.example"));
    }

    [Fact]
    public void A_forged_received_lower_down_never_opens_a_block()
    {
        var forgedReceived = new EmailHeader("Received", $" from x by {Server}; Mon, 16 Nov 2026 09:00:00 +0000");
        var otherTop = new EmailHeader("Received", " from a by mx.other.example; Mon, 16 Nov 2026 09:05:07 +0000");
        Assert.Empty(AuthTrace.ServerResults([otherTop, forgedReceived, ForgedBelow], null, Trusts, out _));
    }

    [Fact]
    public void Untrusted_hosts_and_other_ids_count_nothing()
    {
        var foreign = new EmailHeader("Received", " from a by mx.attacker.example; Mon, 16 Nov 2026 09:05:07 +0000");
        var foreignResults = new EmailHeader("Authentication-Results", " mx.attacker.example; dmarc=pass header.from=bank.example");
        Assert.Empty(AuthTrace.ServerResults([foreign, foreignResults], null, Trusts, out _));

        // inside the server's block, a header under another id is not the server's
        var other = new EmailHeader("Authentication-Results", " relay.news.example; dkim=pass header.d=news.example");
        Assert.Empty(AuthTrace.ServerResults([Received, other, Upstream], null, Trusts, out _));
    }

    [Fact]
    public void Once_pinned_only_the_pinned_id_counts()
    {
        var moved = new EmailHeader("Received", " from a by mx2.example.org; Mon, 16 Nov 2026 09:05:07 +0000");
        var movedResults = new EmailHeader("Authentication-Results", " mx2.example.org; dkim=pass header.d=news.example");
        Assert.NotEmpty(AuthTrace.ServerResults([moved, movedResults], null, Trusts, out _));
        Assert.Empty(AuthTrace.ServerResults([moved, movedResults], Server, Trusts, out _));
        Assert.NotEmpty(AuthTrace.ServerResults([Received, Own], Server, _ => false, out _));
    }

    [Fact]
    public void Imported_mail_without_a_trace_has_no_results()
    {
        Assert.Empty(AuthTrace.ServerResults([Own, ForgedBelow], null, Trusts, out _));
        Assert.Equal(Server, AuthTrace.ReceivedBy(Received.Value));
        Assert.Equal("mx.example.org", AuthTrace.ReceivedBy(" (comment by evil.example) from a by mx.example.org (Stalwart) with ESMTP; date"));
        Assert.Null(AuthTrace.ReceivedBy(" from a; date"));
    }

    [Fact]
    public void Pins_keep_the_first_id_per_account_and_survive_a_restart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "barbican-pins-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var pins = new AuthServPins(dir);
            Assert.True(SpinWait.SpinUntil(() => pins.IsLoaded, 5000));
            Assert.Null(pins.PinFor("acct"));
            pins.Learn("acct", "MX.example.org");
            pins.Learn("acct", "mx.attacker.example");
            Assert.Equal("mx.example.org", pins.PinFor("acct"));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(Path.Combine(dir, "authserv-ids.json")), 5000));

            var again = new AuthServPins(dir);
            Assert.True(SpinWait.SpinUntil(() => again.IsLoaded, 5000));
            Assert.Equal("mx.example.org", again.PinFor("acct"));
            Assert.Null(again.PinFor("other"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
