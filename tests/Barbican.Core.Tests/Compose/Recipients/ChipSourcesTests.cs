#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature (the ported Rust asserts)
using System.Text.Json.Nodes;
using Barbican.Core.Compose.Recipients;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Compose.Recipients;

/// <summary>The 3 pure tests of Rust src/chips.rs, ported (M3 stream composer-ui); the cards are JSContact JSON as the contacts source delivers them.</summary>
public sealed class ChipSourcesTests
{
    [Fact]
    [Red("composer-ui")]
    [RustTest("chips::blur_keeps_partial_text_while_suggestions_are_open")]
    public void Blur_keeps_partial_text_while_suggestions_are_open()
    {
        Assert.False(ChipSources.BlurCommits("sa", true));
        Assert.True(ChipSources.BlurCommits("sa", false));
        Assert.True(ChipSources.BlurCommits("a@b.example", true));
        Assert.True(ChipSources.BlurCommits("Ann <a@b.example>", true));
        Assert.False(ChipSources.BlurCommits("  ", false));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("chips::cards_to_sources")]
    public void Cards_to_sources()
    {
        var cards = new[]
        {
            Card("""{"id": "c1", "uid": "urn:uuid:u1", "name": {"full": "Ann Lee"}, "emails": {"a": {"address": "ann@x.example", "pref": 2}, "b": {"address": "lee@y.example", "pref": 1}}}"""),
            Card("""{"id": "c2", "name": {"components": [{"kind": "given", "value": "Bo"}, {"kind": "surname", "value": "Ek"}]}, "emails": {"a": {"address": "bo@x.example"}}}"""),
            Card("""{"id": "c3", "name": {"full": "No Mail"}}"""),
            Card("""{"id": "g1", "kind": "group", "name": {"full": "Team"}, "members": {"urn:uuid:u1": true, "c2": true, "c3": true, "zz": false}}"""),
        };
        var (people, groups) = ChipSources.SourcesFromCards(cards);
        Assert.Equal([("Ann Lee", "lee@y.example"), ("Ann Lee", "ann@x.example"), ("Bo Ek", "bo@x.example")], people);
        var g = Assert.Single(groups);
        Assert.Equal("Team", g.Name);
        Assert.Equal(["lee@y.example", "bo@x.example"], g.Members.Select(m => m.Email));
    }

    [Fact]
    [Red("composer-ui")]
    [RustTest("chips::recent_dedup")]
    public void Recent_dedup()
    {
        var sent = new[]
        {
            new EmailSummary { Id = "s1", To = [new EmailAddress("A", "a@x.example")], Cc = [new EmailAddress(null, "b@x.example")] },
            new EmailSummary { Id = "s2", To = [new EmailAddress("Old A", "A@X.EXAMPLE"), new EmailAddress(null, " ")] },
        };
        Assert.Equal([("A", "a@x.example"), ("", "b@x.example")], ChipSources.RecentFromSent(sent));
    }

    private static JsonObject Card(string json) => (JsonObject)JsonNode.Parse(json)!;
}
