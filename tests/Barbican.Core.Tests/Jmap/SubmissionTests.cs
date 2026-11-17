// Ported from Rust src/jmap.rs tests reschedule_fallback_order (:1654), from_override_header_identity_and_envelope (:1663),
// addresses (:1878) and delayed_send_needs_futurerelease (:1886). The Submission rules
// (M3 uses them; M1 ports and tests the pure functions).

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Jmap;

public sealed class SubmissionTests
{
    private static JsonObject Cap(string json) => JsonNode.Parse(json)!.AsObject();

    [Fact]
    [RustTest("jmap::reschedule_fallback_order")]
    public void Reschedule_fallback_order()
    {
        Assert.Equal<RescheduleFallback>([new RecreateAt(10_000), new RestoreDraft()], ReschedulePlanner.Fallbacks(10_000, 0));

        // the old time has passed (or is about to): straight back to Drafts
        Assert.Equal<RescheduleFallback>([new RestoreDraft()], ReschedulePlanner.Fallbacks(10_000, 10_000));
        Assert.Equal<RescheduleFallback>([new RestoreDraft()], ReschedulePlanner.Fallbacks(null, 0));
    }

    [Fact]
    [RustTest("jmap::from_override_header_identity_and_envelope")]
    public void From_override_header_identity_and_envelope()
    {
        var ident = new Identity("i1", "Me", "me@a.example.com");
        var o = new Outgoing { From = "me@a.example.com" };
        Assert.Equal(new EmailAddress("Me", "me@a.example.com"), SubmissionRules.HeaderFrom(o, ident, "me@a.example.com"));
        var o2 = o with { FromOverride = new FromOverride("Sales", " sales@a.example.com ") };
        Assert.Equal(new EmailAddress("Sales", "sales@a.example.com"), SubmissionRules.HeaderFrom(o2, ident, "me@a.example.com"));
        var o3 = o with { FromOverride = new FromOverride(string.Empty, "sales@a.example.com") };
        Assert.Equal(new EmailAddress(null, "sales@a.example.com"), SubmissionRules.HeaderFrom(o3, ident, "me@a.example.com"));

        // an override without an address is ignored; a wildcard identity falls back to the login
        var o4 = o with { FromOverride = new FromOverride("X", "nope") };
        Assert.Equal("me@a.example.com", SubmissionRules.HeaderFrom(o4, ident, "me@a.example.com").Email);
        Assert.Equal(new EmailAddress(null, "login@a.example.com"), SubmissionRules.HeaderFrom(o, new Identity(string.Empty, null, "*@a.example.com"), "login@a.example.com"));

        Assert.True(SubmissionRules.WildcardCovers("*@A.example.com", "x@a.example.com"));
        Assert.False(SubmissionRules.WildcardCovers("*@a.example.com", "x@b.example.com"));
        Assert.False(SubmissionRules.WildcardCovers("me@a.example.com", "x@a.example.com"));
        List<Identity> ids = [new("1", null, "me@a.example.com"), new("2", null, "*@b.example.org"), new("3", null, "info@a.example.com")];
        Assert.Equal("3", SubmissionRules.ChooseIdentity(ids, "me@a.example.com", "info@a.example.com")!.Id);
        Assert.Equal("1", SubmissionRules.ChooseIdentity(ids, "me@a.example.com", "sales@a.example.com")!.Id);
        Assert.Equal("2", SubmissionRules.ChooseIdentity(ids, string.Empty, "x@b.example.org")!.Id);
        Assert.Equal("3", SubmissionRules.ChooseIdentity(ids, "3", null)!.Id);
        Assert.Equal("1", SubmissionRules.ChooseIdentity(ids, string.Empty, null)!.Id);

        // an override no identity covers, with no composer identity: refused, not the first
        Assert.Null(SubmissionRules.ChooseIdentity(ids, string.Empty, "x@other.example.net"));
        Assert.Null(SubmissionRules.ChooseIdentity(ids, "gone@a.example.com", "x@other.example.net"));

        // an override without an address is ignored
        Assert.Equal("1", SubmissionRules.ChooseIdentity(ids, string.Empty, "nope")!.Id);

        List<string> r = ["t@x.y"];
        Assert.Null(SubmissionRules.Envelope("me@a.example.com", "ME@a.example.com", null, r));
        var e = SubmissionRules.Envelope("sales@a.example.com", "me@a.example.com", null, r)!;
        Assert.Equal(new EnvelopeAddress("sales@a.example.com"), e.MailFrom);
        Assert.Equal([new EnvelopeAddress("t@x.y")], e.RcptTo);
        var h = SubmissionRules.Envelope("me@a.example.com", "me@a.example.com", 5, r)!;
        Assert.Equal("5", h.MailFrom.Parameters!["HOLDFOR"]);
        Assert.True(SubmissionRules.MailFromRefused(new SetError("x", "forbiddenFrom", null, null)));
        Assert.True(SubmissionRules.MailFromRefused(new SetError("x", "forbiddenMailFrom", null, null)));
        Assert.False(SubmissionRules.MailFromRefused(new SetError("x", "overQuota", null, null)));
    }

    [Fact]
    [RustTest("jmap::addresses")]
    public void Addresses()
    {
        var a = AddressParser.Parse("\"Doe, Jane\" <jane@x.example.org>; bob@y.example.org, <c@z.example.org>, nobody");
        Assert.Equal(3, a.Count);
        Assert.Equal("Doe, Jane", a[0].Name);
        Assert.Equal("c@z.example.org", a[2].Email);
    }

    [Fact]
    [RustTest("jmap::delayed_send_needs_futurerelease")]
    public void Delayed_send_needs_futurerelease()
    {
        Assert.Equal(0, DelayedSend.Limit(Cap("""{"maxDelayedSend": 600}"""), false));
        Assert.Equal(0, DelayedSend.Limit(Cap("""{"maxDelayedSend": 600, "submissionExtensions": {}}"""), false));
        Assert.Equal(600, DelayedSend.Limit(Cap("""{"maxDelayedSend": 600, "submissionExtensions": {"FUTURERELEASE": []}}"""), false));

        // scheduled send: seconds left, rounded up, within the limit
        var hold = DelayedSend.ScheduledHold(10_500, 0, 600);
        Assert.True(hold.IsOk);
        Assert.Equal(11, hold.Value);
        Assert.False(DelayedSend.ScheduledHold(0, 10, 600).IsOk);
        Assert.False(DelayedSend.ScheduledHold(700_000, 0, 600).IsOk);
        Assert.False(DelayedSend.ScheduledHold(10_000, 0, 0).IsOk);
        Assert.Equal(600, DelayedSend.Limit(Cap("""{"maxDelayedSend": 600, "submissionExtensions": ["futurerelease"]}"""), false));
        Assert.Equal(7 * 86400, DelayedSend.Limit(Cap("""{"maxDelayedSend": 2592000, "submissionExtensions": {"FUTURERELEASE": []}}"""), true));
    }
}
