// Ported from Rust src/filters_x.rs tests :723 builds_multi_condition_rule_and_round_trips (T-093), :762 validation (T-094), :783 reorder_and_summary (T-095), :795 sieve_match_semantics (T-096), :805 retroactive_plan (T-097), :836 stale_scan_is_dropped (T-098),
// :843 apply_patches_and_if_in_state (T-099), :869 invalid_regex_is_refused (T-100), :876 odd_header_names_stay_quoted (T-101); then the rows that are not Rust tests: Bulwark COMPARATORS_BY_FIELD (B-SIEVE-017) and G-020 (reject only when the server lists it). M6 stream srv-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Sieve;
using static Barbican.Core.Tests.Filters.Rules.FilterTestData;

namespace Barbican.Core.Tests.Filters.Rules;

public sealed class FilterRulesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Ids(IReadOnlyList<JsonNode> rules) => string.Concat(rules.Select(r => (string?)r["id"]));

    private static FilterBuilt Build(IReadOnlyList<FilterCond> conds, IReadOnlyList<FilterAct> acts, JsonNode? @base = null, string? id = null, string name = "R", bool any = false, bool stop = false, bool spam = false, string from = "", string until = "") =>
        FilterRules.BuildRule(@base, id, name, any, conds, acts, Folders(), stop, spam, from, until);

    [Fact]
    [RustTest("filters_x::builds_multi_condition_rule_and_round_trips")]
    public void Builds_multi_condition_rule_and_round_trips()
    {
        FilterCond[] conds =
        [
            Cond("from", "domain_is", "example.com, example.org"),
            Cond("header", "not_is", "bulk", "Precedence"),
            Cond("size", "greater_than", "10m"),
            Cond("subject", "regex", "^\\[(a|b),c\\]"),
        ];
        FilterAct[] acts = [Act("move", mailbox: "n"), Act("mark_read"), Act("forward", "me@x.example", keepCopy: true)];
        var built = Build(conds, acts, name: "News", any: true, stop: true);
        Assert.Null(built.Error);
        var r = built.Rule!;
        Assert.StartsWith("bw-", (string?)r["id"], StringComparison.Ordinal);
        Assert.Equal("any", (string?)r["matchType"]);
        Assert.Equal("""["example.com","example.org"]""", r["conditions"]![0]!["value"]!.ToJsonString());
        Assert.Equal("Precedence", (string?)r["conditions"]![1]!["headerName"]);
        Assert.Equal("10M", (string?)r["conditions"]![2]!["value"]);
        Assert.Equal("^\\[(a|b),c\\]", (string?)r["conditions"]![3]!["value"]);
        Assert.Equal("News/Letters", (string?)r["actions"]![0]!["value"]);
        Assert.Equal("n", (string?)r["actions"]![0]!["mailboxId"]);
        Assert.Equal(true, (bool?)r["actions"]![2]!["keepCopy"]);

        // the generator accepts it
        var script = SieveScript.GenerateMeta(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(r.DeepClone()) }, string.Empty, ["regex"]);
        Assert.Contains("anyof(address :domain :is \"From\" [\"example.com\", \"example.org\"], not header :is \"Precedence\" \"bulk\", size :over 10M, header :regex \"Subject\"", script, StringComparison.Ordinal);
        Assert.Contains("fileinto \"News/Letters\";", script, StringComparison.Ordinal);
        Assert.Contains("redirect :copy \"me@x.example\";", script, StringComparison.Ordinal);

        // back into the form
        var c2 = FilterRules.ConditionsOf(r);
        Assert.Equal("example.com, example.org", c2[0].Value);
        Assert.Equal("n", FilterRules.ActionsOf(r)[0].Mailbox);

        // editing keeps unknown properties and the id
        var baseRule = r.DeepClone();
        baseRule["origin"] = "bulwark";
        var r2 = Build([c2[0]], [FilterRules.ActionsOf(r)[0]], baseRule, name: "News 2", spam: true).Rule!;
        Assert.Equal((string?)r["id"], (string?)r2["id"]);
        Assert.Equal("bulwark", (string?)r2["origin"]);
        Assert.Equal(true, (bool?)r2["includeSpam"]);
        Assert.Equal("all", (string?)r2["matchType"]);
    }

    [Fact]
    [RustTest("filters_x::validation")]
    public void Validation()
    {
        var mark = new[] { Act("mark_read") };
        var all = new[] { Cond("all", string.Empty) };
        Assert.NotNull(Build([Cond("from", "contains")], mark).Error);
        Assert.Null(Build(all, mark).Error);
        Assert.NotNull(Build(all, [Act("move")]).Error);
        Assert.NotNull(Build(all, [Act("forward", "nope")]).Error);
        Assert.NotNull(Build(all, [Act("reject")]).Error);
        Assert.NotNull(Build([Cond("header", "contains", "x", "Bad Header")], mark).Error);
        Assert.NotNull(Build([Cond("size", "less_than", "lots")], mark).Error);
        Assert.NotNull(Build(all, mark, from: "2026-05-02", until: "2026-05-01").Error);
        var p = Build(all, mark, from: "2026-05-01").Rule!;
        Assert.NotNull(p["activeFrom"]);
        Assert.Null(p["activeUntil"]);
    }

    [Fact]
    [RustTest("filters_x::reorder_and_summary")]
    public void Reorder_and_summary()
    {
        JsonNode[] rules = [J("""{"id": "a"}"""), J("""{"id": "b"}"""), J("""{"id": "c"}""")];
        Assert.Equal("bac", Ids(FilterRules.Moved(rules, "b", -1)));
        Assert.Equal("abc", Ids(FilterRules.Moved(rules, "c", 1)));
        Assert.Equal("bac", Ids(FilterRules.Moved(rules, "a", 1)));
        var r = J("""
            {"matchType": "any", "conditions": [{"field": "from", "comparator": "contains", "value": "x"}, {"field": "attachment", "comparator": "has_any", "value": ""}],
             "actions": [{"type": "star"}, {"type": "move", "value": "Archive"}], "stopProcessing": true}
            """);
        Assert.Equal("From contains x or Has an attachment → Star message, Move to folder Archive · then stop", FilterRules.Summary(r));
    }

    [Fact]
    [RustTest("filters_x::sieve_match_semantics")]
    public void Sieve_match_semantics()
    {
        Assert.True(FilterRules.SieveMatches("news*", "newsletter"));
        Assert.True(FilterRules.SieveMatches("*@example.com", "a@example.com"));
        Assert.True(FilterRules.SieveMatches("a?c", "abc"));
        Assert.False(FilterRules.SieveMatches("a?c", "ac"));
        Assert.True(FilterRules.SieveMatches("a\\*b", "a*b"));
        Assert.False(FilterRules.SieveMatches("a\\*b", "axb"));
    }

    [Fact]
    [RustTest("filters_x::retroactive_plan")]
    public void Retroactive_plan()
    {
        var rule = J("""
            {"matchType": "all", "conditions": [{"field": "from", "comparator": "domain_is", "value": "news.example"}, {"field": "subject", "comparator": "not_contains", "value": "urgent"}],
             "actions": [{"type": "mark_read"}, {"type": "move", "value": "News", "mailboxId": "n"}, {"type": "copy", "value": "Keep", "mailboxId": "k"}]}
            """);
        Assert.Null(FilterRules.RetroUnsupported(rule));
        Assert.True(JsonNode.DeepEquals(J("""{"operator": "AND", "conditions": [{"inMailbox": "inbox"}, {"from": "news.example"}]}"""), FilterRules.QueryFilter(rule, "inbox")));
        var props = FilterRules.RetroProperties(rule);
        Assert.Contains("from", props, StringComparer.Ordinal);
        Assert.Contains("header:Subject:asText:all", props, StringComparer.Ordinal);

        JsonNode Rec(string id, string from, string subject, string keywords, string mailboxes) =>
            FilterRules.RetroMessage(J($$"""{"id": "{{id}}", "from": [{"email": "{{from}}"}], "mailboxIds": {{mailboxes}}, "keywords": {{keywords}}, "header:Subject:asText:all": ["{{subject}}"], "header:Subject:all": [" {{subject}}"]}"""));
        JsonNode[] messages =
        [
            Rec("1", "a@news.example", "Weekly", "{}", """{"inbox": true}"""),
            Rec("2", "b@NEWS.example", "URGENT stuff", "{}", """{"inbox": true}"""),
            Rec("3", "c@news.example", "Monthly", """{"$seen": true}""", """{"inbox": true}"""),
            Rec("4", "d@other.example", "Weekly", "{}", """{"inbox": true}"""),
            Rec("5", "e@news.example", "Spam", """{"$junk": true}""", """{"inbox": true}"""),
        ];
        var steps = FilterRules.Plan(rule, messages);
        Assert.Equal("keyword", steps[0].Kind);
        Assert.Equal(["1"], steps[0].Ids);
        Assert.Equal("$seen", steps[0].Arg);
        Assert.Equal("move", steps[1].Kind);
        Assert.Equal(["1", "3"], steps[1].Ids);
        Assert.Equal("copy", steps[2].Kind);
        Assert.Equal(2, FilterRules.PlanCount(steps));

        // unsupported shapes
        Assert.NotNull(FilterRules.RetroUnsupported(J("""{"conditions": [{"field": "body", "comparator": "contains", "value": "x"}], "actions": [{"type": "star"}]}""")));
        Assert.NotNull(FilterRules.RetroUnsupported(J("""{"conditions": [{"field": "from", "comparator": "contains", "value": "x"}], "actions": [{"type": "forward", "value": "a@b.c"}]}""")));
        Assert.NotNull(FilterRules.RetroUnsupported(J("""{"activeFrom": "2026-01-01T00:00:00Z", "conditions": [{"field": "from", "comparator": "contains", "value": "x"}], "actions": [{"type": "star"}]}""")));
        Assert.NotNull(FilterRules.RetroUnsupported(J("""{"conditions": [{"field": "from", "comparator": "contains", "value": "x"}], "actions": [{"type": "move", "value": "X"}]}""")));
    }

    [Fact]
    [RustTest("filters_x::stale_scan_is_dropped")]
    public void Stale_scan_is_dropped()
    {
        Assert.True(FilterRules.Current(2, 2, true));
        Assert.False(FilterRules.Current(3, 2, true)); // rule B started after rule A
        Assert.False(FilterRules.Current(2, 2, false)); // dialog closed
    }

    [Fact]
    [RustTest("filters_x::apply_patches_and_if_in_state")]
    public async Task Apply_patches_and_if_in_state()
    {
        Assert.Equal("""{"mailboxIds/a~1b~0c":true}""", FilterRules.StepPatch("copy", "a/b~c").ToJsonString());
        Assert.Equal("""{"keywords/$label:x~1y":true}""", FilterRules.StepPatch("keyword", "$label:x/y").ToJsonString());
        Assert.Equal("""{"mailboxIds":{"m1":true}}""", FilterRules.StepPatch("move", "m1").ToJsonString());

        FilterPlanStep[] steps = [new("keyword", ["e1", "e2", "e3"], "$seen"), new("copy", ["e1"], "m2")];
        var sent = new List<JsonArray>();
        Task<Result<JsonArray>> SendAsync(JsonArray call)
        {
            sent.Add((JsonArray)call.DeepClone());
            var updated = new JsonObject();
            foreach (var kv in call[1]!["update"]!.AsObject())
            {
                updated[kv.Key] = null;
            }

            return Task.FromResult(Result<JsonArray>.Ok(new JsonArray("Email/set", new JsonObject { ["updated"] = updated, ["newState"] = "s" + sent.Count }, "a")));
        }

        var r = await FilterRules.ApplyAsync(SendAsync, "A", "s0", steps, 2, Ct);
        Assert.Equal(new FilterApplied(FilterAppliedKind.Done, 3, null), r);
        Assert.Equal(3, sent.Count);
        Assert.Equal("s0", (string?)sent[0][1]!["ifInState"]);
        Assert.Equal("s1", (string?)sent[1][1]!["ifInState"]);
        Assert.Equal("s2", (string?)sent[2][1]!["ifInState"]);
        Assert.Equal("""{"mailboxIds/m2":true}""", sent[2][1]!["update"]!["e1"]!.ToJsonString());

        // the state moved on: stop and say how much was done
        var n = 0;
        var stale = await FilterRules.ApplyAsync(_ =>
        {
            n++;
            return Task.FromResult(Result<JsonArray>.Ok(n == 1
                ? new JsonArray("Email/set", new JsonObject { ["updated"] = new JsonObject { ["e1"] = null, ["e2"] = null }, ["newState"] = "s1" }, "a")
                : new JsonArray("error", new JsonObject { ["type"] = "stateMismatch" }, "a")));
        }, "A", "s0", steps, 2, Ct);
        Assert.Equal(new FilterApplied(FilterAppliedKind.Stale, 2, null), stale);
        Assert.Contains("2 of 3", FilterRules.AppliedText(stale, 3), StringComparison.Ordinal);

        var failed = await FilterRules.ApplyAsync(_ => Task.FromResult(Result<JsonArray>.Fail(new ValidationError("network down"))), "A", "s0", steps, 2, Ct);
        Assert.Equal("Applied to 0 of 3 messages: network down", FilterRules.AppliedText(failed, 3));
        Assert.Equal("Rule applied to 1 message", FilterRules.AppliedText(new FilterApplied(FilterAppliedKind.Done, 1, null), 1));
    }

    [Fact]
    [RustTest("filters_x::invalid_regex_is_refused")]
    public void Invalid_regex_is_refused()
    {
        var bad = Build([Cond("subject", "regex", "([a-z")], [Act("mark_read")]);
        Assert.Contains("not a valid regular expression", bad.Error, StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("filters_x::odd_header_names_stay_quoted")]
    public void Odd_header_names_stay_quoted()
    {
        FilterAct[] acts = [Act("mark_read")];
        // names the generator would silently skip are refused in the editor
        foreach (var bad in new[] { "X-A\"b", "X\\c", "X.Y", "X Y", "X:Y" })
        {
            Assert.NotNull(Build([Cond("header", "contains", "v", bad)], acts).Error);
        }

        var r = Build([Cond("header", "contains", "v\"x\\y", "X-Spam_Flag")], acts).Rule!;
        var script = SieveScript.GenerateMeta(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(r.DeepClone()) }, string.Empty, []);
        Assert.Contains("if header :contains \"X-Spam_Flag\" \"v\\\"x\\\\y\" {", script, StringComparison.Ordinal);
        var p = FilterRules.RetroProperties(r);
        Assert.Contains("header:X-Spam_Flag:asText:all", p, StringComparer.Ordinal);
        Assert.Contains("header:X-Spam_Flag:all", p, StringComparer.Ordinal);
        var m = FilterRules.RetroMessage(J("""{"id": "e", "header:X-Spam_Flag:asText:all": ["v\"x\\y"], "header:X-Spam_Flag:all": [" v\"x\\y"]}"""));
        Assert.Equal("v\"x\\y", (string?)m["headers"]!["x-spam_flag"]![0]);
    }

    [Fact]
    public void Comparators_follow_the_field()
    {
        Assert.Equal(10, FilterRules.Comparators("from").Count);
        Assert.Equal(8, FilterRules.Comparators("subject").Count);
        Assert.Equal(["contains", "is", "regex"], FilterRules.Comparators("body"));
        Assert.Equal(["greater_than", "less_than"], FilterRules.Comparators("size"));
        Assert.Equal(["has_any", "has_type"], FilterRules.Comparators("attachment"));
        Assert.Empty(FilterRules.Comparators("all"));
        Assert.Equal(9, FilterRules.Fields.Count);
    }

    [Fact]
    public void Reject_is_offered_only_when_the_server_lists_the_extension()
    {
        Assert.DoesNotContain(FilterRules.ActionKinds(["fileinto"]), a => string.Equals(a.Id, "reject", StringComparison.Ordinal));
        Assert.Contains(FilterRules.ActionKinds(["fileinto", "reject"]), a => string.Equals(a.Id, "reject", StringComparison.Ordinal));
        Assert.Equal(9, FilterRules.ActionKinds([]).Count);
        Assert.Equal(10, FilterRules.ActionKinds(["reject"]).Count);
    }
}
