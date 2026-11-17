#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Jmap.Submission;

namespace Barbican.Core.Tests.Compose.Recipients;

/// <summary>The 8 tests of Rust src/recipients.rs plus compose::recipients_round_trip, ported (M3 stream composer-ui). Addresses are fictional.</summary>
public sealed class RecipientTextTests
{
    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::idn_round_trip")]
    public void Idn_round_trip()
    {
        Assert.Equal("jo@xn--bcher-kva.example", RecipientText.ToAsciiEmail("jo@bücher.example"));
        Assert.Equal("Jo@Example.COM", RecipientText.ToAsciiEmail("Jo@Example.COM"));
        Assert.Equal("jo@bücher.example", RecipientText.ToUnicodeEmail("jo@xn--bcher-kva.example"));
        Assert.Equal("jo@bücher.example", RecipientText.ToUnicodeEmail("jo@XN--BCHER-KVA.EXAMPLE"));

        // not canonical / broken punycode stays as it is
        Assert.Equal("xn--zz-zz.de", RecipientText.ToUnicodeDomain("xn--zz-zz.de"));
        Assert.Null(RecipientText.ToAsciiDomain("a b.de"));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::validation")]
    public void Validation()
    {
        Assert.True(RecipientText.IsValidEmail("a.b+tag@example.org"));
        Assert.True(RecipientText.IsValidEmail("jo@bücher.example"));
        foreach (var bad in new[] { "", "a", "a@", "@b.c", "a@b..c", "a@-b.c", "a@b.c.", "a b@c.d", "a@b@c.d", "a<b@c.d", "user\n@x.y" })
        {
            Assert.False(RecipientText.IsValidEmail(bad), bad);
        }

        Assert.False(RecipientText.IsValidEmail(new string('a', 65) + "@x.y"));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::mailbox_split")]
    public void Mailbox_split()
    {
        Assert.Equal(("Jane", "j@x.example"), RecipientText.SplitMailbox("Jane <j@x.example>"));
        Assert.Equal(("Jane", "j@x.example"), RecipientText.SplitMailbox("Jane<j@x.example"));
        Assert.Equal(("Jane", "j@x.example"), RecipientText.SplitMailbox("\"Jane <j@x.example>\" <j@x.example>"));
        Assert.Equal(("", "j@x.example"), RecipientText.SplitMailbox("j@x.example"));
        Assert.Equal("Doe, J", RecipientText.SanitizeDisplayName("\"Doe, J\""));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::split_and_parse")]
    public void Split_and_parse()
    {
        Assert.Equal(["\"Doe, John\" <j@d.o>", "x@y.z"], RecipientText.SplitRecipients("\"Doe, John\" <j@d.o>, x@y.z", ","));
        Assert.Equal(["Team: a@x.example, b@y.example;", "c@z.example"], RecipientText.SplitRecipients("Team: a@x.example, b@y.example;, c@z.example", ","));

        // a colon no ';' closes is not a group
        Assert.Equal(["Support: help@x.example", "bob@y.example"], RecipientText.SplitRecipients("Support: help@x.example, bob@y.example", ","));
        Assert.Equal(["Team: a@x.example;", "Re: Jo <jo@x.example>", "b@y.example"], RecipientText.SplitRecipients("Team: a@x.example;, Re: Jo <jo@x.example>, b@y.example", ","));

        // an unclosed < does not swallow the rest
        Assert.Equal(["Jo <jo@x.example", "b@y.example"], RecipientText.SplitRecipients("Jo <jo@x.example, b@y.example", ","));
        Assert.Equal(["\"Sup\\\", ceo@c.example, x\" <s@a.b>"], RecipientText.SplitRecipients("\"Sup\\\", ceo@c.example, x\" <s@a.b>", ","));
        var g = RecipientText.ParseRecipient("\"Team, A\": Ann <a@x.example>, b@y.example;");
        Assert.Equal("Team, A", g.Name);
        Assert.Equal(2, g.Group!.Count);
        Assert.Null(RecipientText.ParseRecipient("Subject: hello;").Group);
        Assert.Equal(Recipient.Addr("Doe, J", "j@d.o"), RecipientText.ParseRecipient("\"Doe, J\" <j@d.o>"));

        // format and parse round-trip, groups included
        Recipient[] list = [Recipient.Addr("Doe, \"J\"", "j@d.o"), g, Recipient.Addr("", "x@y.z")];
        Assert.Equal(list, RecipientText.ParseList(RecipientText.FormatList(list)));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::expansion_and_invalid")]
    public void Expansion_and_invalid()
    {
        var v = RecipientText.ParseList("Ann <a@x.example>, Team: a@x.example, B <b@y.example>;, nope");
        var e = RecipientText.Expand(v);
        Assert.Equal(["a@x.example", "b@y.example", "nope"], e.Select(r => r.Email));
        Assert.Equal(["nope"], RecipientText.InvalidAddresses(v));
        Assert.Equal("Jo <jo@xn--bcher-kva.example>, b@y.example", RecipientText.ExpandForSend("Team: Jo <jo@bücher.example>, b@y.example;"));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::paste")]
    public void Paste()
    {
        var (v, inv) = RecipientText.SplitPasted("a@x.example b@y.example; \"Doe, J\" <j@d.o>\nC <c@z.example, junk, A@X.EXAMPLE", ["old@q.example"]);
        Assert.Equal(["a@x.example", "b@y.example", "j@d.o", "c@z.example"], v.Select(r => r.Email));
        Assert.Equal("Doe, J", v[2].Name);
        Assert.Equal("C", v[3].Name);
        Assert.Equal(["junk"], inv);
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::ranking")]
    public void Ranking()
    {
        var src = new SuggestionSources
        {
            Contacts = [("Daniel", "dan@x.example"), ("Anna", "anna@x.example"), ("Jane <jane@x.example>", "jane@x.example")],
            Groups = [("g1", "Annual team", [Recipient.Addr("", "a@x.example")]), ("g2", "Empty an", [])],
            Recent = [("", "anna@x.example"), ("Anders", "and@y.example")],
        };
        var s = RecipientText.Suggest("an", src);
        var keys = s.Select(x => x.Group is not null ? "g:" + x.Name : x.Email);

        // word starts first (group, Anna, Anders), then Daniel and Jane which only contain it
        Assert.Equal(["g:Annual team", "anna@x.example", "and@y.example", "dan@x.example", "jane@x.example"], keys);
        Assert.Equal("Jane", RecipientText.Suggest("jane", src)[0].Name);
        Assert.Empty(RecipientText.Suggest(" ", src));
        Assert.Equal(1, RecipientText.MatchRank("ül", ["Müller", "x"]));
        Assert.Equal(0, RecipientText.MatchRank("mü", ["Hans Müller"]));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("recipients::default_pick")]
    public void Default_pick()
    {
        Suggestion[] s = [new("A", "a@x.example"), new("B", "b@x.example")];
        Assert.Equal(1, RecipientText.DefaultSuggestionIndex("b@x.example", s));
        Assert.Equal(-1, RecipientText.DefaultSuggestionIndex("c@x.example", s));
        Assert.Equal(0, RecipientText.DefaultSuggestionIndex("an", s));
        Assert.Equal(-1, RecipientText.DefaultSuggestionIndex("", s));
        Assert.Equal("MO", RecipientText.Initials("Mira Okafor", ""));
        Assert.Equal("JW", RecipientText.Initials("", "jonas.weber@x.example"));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("compose::recipients_round_trip")]
    public void Recipients_round_trip()
    {
        Assert.Equal("a@b.c", RecipientText.FormatRecipient("", "a@b.c"));
        Assert.Equal("a@b.c", RecipientText.FormatRecipient("a@b.c", "a@b.c"));
        Assert.Equal("Jane Doe <j@d.o>", RecipientText.FormatRecipient("Jane Doe", "j@d.o"));
        var odd = RecipientText.FormatRecipient("Doe, \"J\"", "j@d.o");
        Assert.Equal("\"Doe, \\\"J\\\"\" <j@d.o>", odd);
        var back = AddressParser.Parse(odd + ", x@y.z");
        Assert.Equal(2, back.Count);
        Assert.Equal("Doe, \"J\"", back[0].Name);
        Assert.Equal("j@d.o", back[0].Email);
    }
}
