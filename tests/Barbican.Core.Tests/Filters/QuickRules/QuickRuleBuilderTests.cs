// Ported from Rust src/quick_rules.rs tests (:249-:348). Red until stream search implements QuickRuleBuilder (M2 plan 6.5, T-207..T-211).

using System.Text.Json.Nodes;
using Barbican.Core.Filters.QuickRules;
using Barbican.Core.Sieve;

namespace Barbican.Core.Tests.Filters.QuickRules;

public sealed class QuickRuleBuilderTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static QuickRuleSubject Subj(params string[] senders) => new(senders, "acme.example", "news.acme.example");

    private static QuickRuleFolder News() => new("m1", "INBOX/News", "News");

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(J(expected), actual), actual?.ToJsonString());

    [Fact]
    [Red("search")]
    [RustTest("quick_rules::hand_edited_script_is_refused")]
    public void Hand_edited_script_is_refused()
    {
        var cand = J("""{"name": "x", "conditions": [], "actions": []}""");
        var hand = SieveScript.Parse("/* @metadata:begin\n{\"version\": 9, \"rules\": []}\n@metadata:end */\nif true { stop; }");
        var refused = QuickRuleBuilder.Plan(hand, cand);
        Assert.False(refused.IsOk);
        Assert.Contains("edited by hand", refused.Error!.Message, StringComparison.Ordinal);

        var broken = SieveScript.Parse("/* @metadata:begin\nnot json\n@metadata:end */");
        Assert.False(QuickRuleBuilder.Plan(broken, cand).IsOk);

        var fresh = SieveScript.Parse(string.Empty);
        var planned = QuickRuleBuilder.Plan(fresh, cand);
        Assert.True(planned.IsOk && planned.Value!.NeedsSave, "an empty script takes the rule");
    }

    [Fact]
    [Red("search")]
    [RustTest("quick_rules::subject_from_a_message")]
    public void Subject_from_a_message()
    {
        var m = J("""{"from": [{"email": "Anna@acme.example", "name": "Anna"}], "headers": [{"name": "List-Id", "value": " Weekly news <news.acme.example>"}]}""");
        var s = QuickRuleBuilder.SubjectOf(m, ["me@x.example"]);
        Assert.Equal(["anna@acme.example"], s.Senders);
        Assert.Equal("acme.example", s.Domain);
        Assert.Equal("news.acme.example", s.ListId);
        var own = QuickRuleBuilder.SubjectOf(J("""{"from": [{"email": "me@x.example"}]}"""), ["ME@x.example"]);
        Assert.True(own.Senders.Count == 0 && own.Domain is null);
        Assert.Equal("plain.list.id", QuickRuleBuilder.ExtractListId("plain.list.id"));
        Assert.Null(QuickRuleBuilder.ExtractListId("has space"));
    }

    [Fact]
    [Red("search")]
    [RustTest("quick_rules::sieve_paths")]
    public void Sieve_paths()
    {
        SieveFolderRef[] f =
        [
            new("i", "Inbox", null, "inbox"),
            new("n", "News", "i", string.Empty),
            new("a", "Archive", null, "archive"),
        ];
        Assert.Equal("INBOX/News", QuickRuleBuilder.SievePath(f, "n"));
        Assert.Equal("Archive", QuickRuleBuilder.SievePath(f, "a"));
    }

    [Fact]
    [Red("search")]
    [RustTest("quick_rules::presets_build_bulwark_rules")]
    public void Presets_build_bulwark_rules()
    {
        var s = Subj("anna@acme.example");
        var r = QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveSender(News()), s, "r1");
        AssertJson("""
            {"id": "r1", "name": "Move from anna@acme.example to News", "enabled": true, "matchType": "all",
             "conditions": [{"field": "from", "comparator": "address_is", "value": "anna@acme.example"}],
             "actions": [{"type": "move", "value": "INBOX/News", "mailboxId": "m1"}], "stopProcessing": true}
            """, r);
        var d = QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveDomain(News()), s, "r2");
        AssertJson("""{"field": "from", "comparator": "domain_is", "value": "acme.example"}""", d["conditions"]![0]);
        Assert.Equal("Move from @acme.example to News", (string)d["name"]!);
        var l = QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveList(News()), s, "r3");
        AssertJson("""{"field": "header", "headerName": "List-Id", "comparator": "contains", "value": "news.acme.example"}""", l["conditions"]![0]);
        var b = QuickRuleBuilder.PresetRule(new QuickRulePreset.Block(new QuickRuleFolder("j", "Junk Mail", "Junk Mail")), s, "r4");
        Assert.True((bool)b["includeSpam"]!);
        Assert.Equal("Block anna@acme.example", (string)b["name"]!);
        var t = QuickRuleBuilder.PresetRule(new QuickRulePreset.Tag("work", "Work"), Subj("a@acme.example", "b@acme.example"), "r5");
        Assert.Equal("Tag as Work from 2 senders", (string)t["name"]!);
        AssertJson("""["a@acme.example", "b@acme.example"]""", t["conditions"]![0]!["value"]);
        AssertJson("""{"type": "add_label", "value": "work"}""", t["actions"]![0]);

        // the generator turns it into Sieve
        var script = SieveScript.Generate([r], string.Empty);
        Assert.Contains("address :is \"From\" \"anna@acme.example\"", script, StringComparison.Ordinal);
        Assert.Contains("""fileinto "INBOX/News";""", script, StringComparison.Ordinal);
    }

    [Fact]
    [Red("search")]
    [RustTest("quick_rules::apply_creates_merges_or_covers")]
    public void Apply_creates_merges_or_covers()
    {
        var s = Subj("anna@acme.example");
        var first = QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveSender(News()), s, "r1");
        var created = Assert.IsType<QuickRuleOutcome.Created>(QuickRuleBuilder.Apply([], first));
        var rules = created.Rules;
        Assert.Single(rules);

        // the same sender again: covered
        var covered = Assert.IsType<QuickRuleOutcome.Covered>(QuickRuleBuilder.Apply(rules, QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveSender(News()), s, "r2")));
        Assert.Equal("Move from anna@acme.example to News", covered.Name);

        // another sender to the same folder: merged into the first rule, keeping its name
        var bob = QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveSender(News()), Subj("Bob@acme.example"), "r3");
        var merged = Assert.IsType<QuickRuleOutcome.Merged>(QuickRuleBuilder.Apply(rules, bob));
        Assert.Equal("Move from anna@acme.example to News", merged.Name);
        Assert.Single(merged.Rules);
        AssertJson("""["anna@acme.example", "Bob@acme.example"]""", merged.Rules[0]["conditions"]![0]!["value"]);

        // another folder: a new rule at the top
        var other = new QuickRuleFolder("m2", "Other", "Other");
        var two = Assert.IsType<QuickRuleOutcome.Created>(QuickRuleBuilder.Apply(merged.Rules, QuickRuleBuilder.PresetRule(new QuickRulePreset.MoveSender(other), s, "r4")));
        Assert.Equal("r4", (string)two.Rules[0]["id"]!);

        // a disabled rule, or one with a period, is never the one
        var off = rules.Select(r => r.DeepClone()).ToList();
        off[0]["enabled"] = false;
        Assert.IsType<QuickRuleOutcome.Created>(QuickRuleBuilder.Apply(off, bob));
        var timed = rules.Select(r => r.DeepClone()).ToList();
        timed[0]["activeUntil"] = "2026-12-01T00:00:00.000Z";
        Assert.IsType<QuickRuleOutcome.Created>(QuickRuleBuilder.Apply(timed, bob));
    }
}
