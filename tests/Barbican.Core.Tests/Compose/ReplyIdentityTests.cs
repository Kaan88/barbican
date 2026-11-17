using Barbican.Core.Compose;
using Barbican.Core.Jmap.Submission;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust src/compose.rs test reply_identity_order, ported on the typed identity list and <see cref="ReplyFrom"/> (D-241; M3 stream send-pipeline).</summary>
public sealed class ReplyIdentityTests
{
    private static readonly Identity[] Ids = [new("i0", null, "me@a.example"), new("i1", null, "info@b.example"), new("i2", null, "me+news@a.example")];

    private static ReplyFrom From(string[] recipients, string[] delivered, bool forward) => new() { Recipients = recipients, Delivered = delivered, Forward = forward };

    [Fact]
    [RustTest("compose::reply_identity_order")]
    public void Reply_identity_order()
    {
        // own identity in the recipients, always
        Assert.Equal(new ReplyIdentityChoice(1, null), ReplyIdentity.Resolve(Ids, From(["x@y.z", "INFO@b.example"], [], false), false, "exact"));

        // sub-address tag: the untagged identity wins
        Assert.Equal(0, ReplyIdentity.Resolve(Ids, From(["me+shop@a.example"], [], false), false, "exact")?.Index);

        // catch-all on a user's domain only with the setting and domain matching; it rewrites From
        Assert.Equal(new ReplyIdentityChoice(1, new FromOverride("", "Sales@b.example")), ReplyIdentity.Resolve(Ids, From(["Sales@b.example"], [], false), true, "domain"));
        Assert.Null(ReplyIdentity.Resolve(Ids, From(["sales@b.example"], [], false), true, "exact"));
        Assert.Null(ReplyIdentity.Resolve(Ids, From(["sales@b.example"], [], false), false, "domain"));

        // the name the sender gave the alias comes along
        var named = From(["sales@b.example"], [], false) with { Names = [("sales@b.example", "B Sales")] };
        Assert.Equal(new ReplyIdentityChoice(1, new FromOverride("B Sales", "sales@b.example")), ReplyIdentity.Resolve(Ids, named, true, "domain"));

        // a catch-all address only in Delivered-To also becomes the override
        Assert.Equal(new ReplyIdentityChoice(0, new FromOverride("", "help@a.example")), ReplyIdentity.Resolve(Ids, From(["list@x.example"], ["help@a.example"], false), true, "domain"));

        // never on a forward
        Assert.Null(ReplyIdentity.Resolve(Ids, From(["sales@b.example"], [], true), true, "domain"));

        // Delivered-To after the headers
        Assert.Equal(new ReplyIdentityChoice(1, null), ReplyIdentity.Resolve(Ids, From(["list@x.example"], ["info@b.example"], false), false, "exact"));

        // self-sent: keep the sending identity
        var s = new ReplyFrom { SelfSent = "info@b.example", Recipients = ["me@a.example"] };
        Assert.Equal(1, ReplyIdentity.Resolve(Ids, s, false, "exact")?.Index);

        // a draft keeps its identity, or its own sender as the override
        static ReplyFrom Draft(string e) => new() { DraftFrom = new FromOverride("Name", e) };
        Assert.Equal(new ReplyIdentityChoice(1, null), ReplyIdentity.Resolve(Ids, Draft("info@b.example"), false, "exact"));
        Assert.Equal(new ReplyIdentityChoice(1, new FromOverride("Name", "alias@b.example")), ReplyIdentity.Resolve(Ids, Draft("alias@b.example"), false, "exact"));
    }
}
