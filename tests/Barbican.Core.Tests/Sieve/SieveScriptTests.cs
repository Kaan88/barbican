// Ported from Rust src/sieve.rs tests (:642-:804). M6 rows T-271..T-279 ported early because quick rules call the Sieve parser and generator
// (M2 plan 2.1). Red until stream search implements SieveScript (M2 plan 6.5).

using System.Text.Json.Nodes;
using Barbican.Core.Sieve;

namespace Barbican.Core.Tests.Sieve;

public sealed class SieveScriptTests
{
    private const string FromT = """anyof(currentdate :zone "+0000" :value "gt" "date" "2026-10-05", allof(currentdate :zone "+0000" :is "date" "2026-10-05", currentdate :zone "+0000" :value "ge" "time" "06:00:00"))""";

    private const string UntilT = """anyof(currentdate :zone "+0000" :value "lt" "date" "2026-10-16", allof(currentdate :zone "+0000" :is "date" "2026-10-16", currentdate :zone "+0000" :value "le" "time" "16:00:00"))""";

    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static string ReplaceFirst(string text, string find, string with)
    {
        var i = text.IndexOf(find, StringComparison.Ordinal);
        return i < 0 ? text : string.Concat(text.AsSpan(0, i), with, text.AsSpan(i + find.Length));
    }

    private static List<JsonNode> Rules(JsonNode meta) => [.. ((JsonArray)meta["rules"]!).Select(n => n!.DeepClone())];

    [Fact]
    [RustTest("sieve::external_keeps_elsif_else_and_text_strings")]
    public void External_keeps_elsif_else_and_text_strings()
    {
        var ext = "if header :contains \"subject\" \"a\" { fileinto \"A\"; } elsif header :contains \"subject\" \"b\" { fileinto \"B\"; } else { keep; }\nreject text:\nNo; thanks.\n# not a comment\n.\n;\nkeep;";
        var script = "/* @metadata:begin\n{\"version\":1,\"rules\":[]}\n@metadata:end */\n" + SieveScript.External + "\n" + ext;
        var p = SieveScript.Parse(script);
        Assert.Contains("else { keep; }", p.External, StringComparison.Ordinal);
        Assert.Contains("reject text:\nNo; thanks.\n# not a comment\n.\n;", p.External, StringComparison.Ordinal);
        var blocks = SieveScript.ScanTopLevel(ext);
        Assert.Equal(3, blocks.Count);
        Assert.EndsWith("else { keep; }", blocks[0].Raw.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("sieve::vacation_only_scripts")]
    public void Vacation_only_scripts()
    {
        var s = "require [\"vacation\"];\n# auto\nvacation :days 1 :subject \"Away \\\"now\\\"\" \"I am out.\";\n";
        var p = SieveScript.Parse(s);
        Assert.True(JsonNode.DeepEquals(J("""{"isEnabled": true, "subject": "Away \"now\"", "textBody": "I am out."}"""), p.Metadata["vacation"]));
        Assert.True(p.External.Length == 0 && !p.Locked);

        // with a test it is a rule, not the auto-reply
        Assert.Null(SieveScript.VacationOnly("if true { vacation \"x\"; }"));

        // a word "if" inside a string does not count
        Assert.Equal("if you need me", (string)SieveScript.VacationOnly("vacation \"if you need me\";")!["textBody"]!);
        Assert.Null(SieveScript.VacationOnly("keep;"));
    }

    [Fact]
    [RustTest("sieve::hand_written_blocks_between_generated_ones_survive")]
    public void Hand_written_blocks_between_generated_ones_survive()
    {
        var rule = SieveScript.Rule("News", "from", "contains", "n@", "move", "N", false);
        var meta = new JsonObject
        {
            ["version"] = 1,
            ["rules"] = new JsonArray(rule),
            ["vacation"] = J("""{"isEnabled": true, "subject": "", "textBody": "away"}"""),
        };
        var g = SieveScript.GenerateMeta(meta, string.Empty, []);

        // someone adds a block between the vacation and the rules, and one at the end
        var edited = ReplaceFirst(g, "\n# Rule: News", "\n# mine\nif header :contains \"subject\" \"x\" { discard; }\n\n# Rule: News") + "\nif true { keep; }\n";
        var p = SieveScript.Parse(edited);
        Assert.Contains("# mine\nif header :contains \"subject\" \"x\" { discard; }", p.External, StringComparison.Ordinal);
        Assert.Contains("if true { keep; }", p.External, StringComparison.Ordinal);
        Assert.DoesNotContain("# Rule: News", p.External, StringComparison.Ordinal);
        Assert.DoesNotContain("Vacation auto-reply", p.External, StringComparison.Ordinal);

        // saving again keeps them exactly once
        var again = SieveScript.GenerateMeta(p.Metadata, p.External, []);
        Assert.Equal(1, again.Split("# mine").Length - 1);
        Assert.Equal(p.External, SieveScript.Parse(again).External);
    }

    [Fact]
    [RustTest("sieve::roundtrip_matches_bulwark_shape")]
    public void Roundtrip_matches_bulwark_shape()
    {
        var r = SieveScript.Rule("News", "from", "contains", "newsletter@", "move", "Newsletters", false);
        var s = SieveScript.Generate([r], "# rule:[ext]\nif true { keep; }");
        Assert.StartsWith("/* @metadata:begin\n{", s, StringComparison.Ordinal);
        Assert.Contains("\"version\":1", s, StringComparison.Ordinal);
        Assert.Contains("@metadata:end */", s, StringComparison.Ordinal);
        Assert.Contains("require [\"fileinto\"];", s, StringComparison.Ordinal);
        Assert.Contains("if header :contains \"From\" \"newsletter@\" {\n    fileinto \"Newsletters\";\n}", s, StringComparison.Ordinal);
        Assert.Contains("# --- External rules (managed outside Bulwark) ---", s, StringComparison.Ordinal);
        var p = SieveScript.Parse(s);
        Assert.Equal("News", (string)p.Metadata["rules"]![0]!["name"]!);
        Assert.Contains("# rule:[ext]", p.External, StringComparison.Ordinal);
        Assert.False(p.Locked);

        string[] ext = ["spamtestplus", "relational"];
        var meta = new JsonObject
        {
            ["version"] = 1,
            ["rules"] = new JsonArray(SieveScript.Rule("N", "from", "contains", "x", "move", "F", false)),
        };
        var g = SieveScript.GenerateMeta(meta, string.Empty, ext);
        Assert.Contains("""if allof(header :contains "From" "x", not spamtest :percent :value "ge" :comparator "i;ascii-numeric" "50")""", g, StringComparison.Ordinal);
        Assert.Contains("\"comparator-i;ascii-numeric\"", g, StringComparison.Ordinal);
    }

    // period.test.ts 'writes the start and end as UTC date and time tests'
    [Fact]
    [RustTest("sieve::period_tests_match_bulwark")]
    public void Period_tests_match_bulwark()
    {
        var t = SieveScript.PeriodTests(J("""{"activeFrom": "2026-10-05T08:00:00+02:00", "activeUntil": "2026-10-16T16:00:00.000Z"}"""));
        Assert.Equal([FromT, UntilT], t);
        Assert.Empty(SieveScript.PeriodTests(J("{}"))!);
        Assert.Null(SieveScript.PeriodTests(J("""{"activeFrom": "2026-10-05T08:00"}""")));
        Assert.Null(SieveScript.PeriodTests(J("""{"activeFrom": "2026-10-05T06:00:00Z", "activeUntil": "soon"}""")));
        Assert.NotNull(SieveScript.PeriodBoundaryOf("2026-10-05T06:00Z"));
    }

    // Expected text written from generator.ts / period.test.ts ('confines the rule to its period').
    [Fact]
    [RustTest("sieve::rule_with_period_matches_bulwark")]
    public void Rule_with_period_matches_bulwark()
    {
        var rule = J("""
            {"id": "r1", "name": "Fwd", "enabled": true, "matchType": "all",
             "conditions": [{"field": "from", "comparator": "contains", "value": "@"}],
             "actions": [{"type": "forward", "value": "kollege@example.com"}, {"type": "discard"}],
             "stopProcessing": false, "activeFrom": "2026-10-05T06:00:00.000Z", "activeUntil": "2026-10-16T16:00:00.000Z"}
            """);
        var meta = new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(rule) };
        var got = SieveScript.GenerateMeta(meta, string.Empty, []);
        const string MetaJson = """{"version":2,"rules":[{"id":"r1","name":"Fwd","enabled":true,"matchType":"all","conditions":[{"field":"from","comparator":"contains","value":"@"}],"actions":[{"type":"forward","value":"kollege@example.com"},{"type":"discard"}],"stopProcessing":false,"activeFrom":"2026-10-05T06:00:00.000Z","activeUntil":"2026-10-16T16:00:00.000Z"}]}""";
        var want = "/* @metadata:begin\n" + MetaJson + "\n@metadata:end */\n\nrequire [\"date\", \"relational\"];\n\n# Rule: Fwd\nif allof(" + FromT + ", " + UntilT
            + ", header :contains \"From\" \"@\") {\n    redirect \"kollege@example.com\";\n    discard;\n}\n";
        Assert.Equal(want, got);
        var p = SieveScript.Parse(got);
        Assert.False(p.Locked);
        Assert.Equal(got, SieveScript.GenerateMeta(p.Metadata, p.External, []));

        // spam guard inside the period (period.test.ts 'keeps the conditions and the spam guard intact')
        string[] ext = ["fileinto", "mailbox", "mailboxid", "spamtestplus", "relational", "comparator-i;ascii-numeric", "date", "copy"];
        var any = J("""
            {"id": "r1", "name": "Fwd", "enabled": true, "matchType": "any",
             "conditions": [{"field": "from", "comparator": "contains", "value": "a"}, {"field": "subject", "comparator": "contains", "value": ["b", "c"]}],
             "actions": [{"type": "move", "value": "F", "mailboxId": "mb"}], "stopProcessing": false,
             "activeFrom": "2026-10-05T06:00:00.000Z", "activeUntil": "2026-10-16T16:00:00.000Z"}
            """);
        var g = SieveScript.GenerateMeta(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(any) }, string.Empty, ext);
        Assert.Contains("if allof(" + FromT + ", " + UntilT
            + ", allof(anyof(header :contains \"From\" \"a\", header :contains \"Subject\" [\"b\", \"c\"]), not spamtest :percent :value \"ge\" :comparator \"i;ascii-numeric\" \"50\")) {", g, StringComparison.Ordinal);
        Assert.Contains("""require ["comparator-i;ascii-numeric", "date", "fileinto", "mailbox", "mailboxid", "relational", "spamtestplus"];""", g, StringComparison.Ordinal);
        Assert.Contains("""    fileinto :mailboxid "mb" "F";""", g, StringComparison.Ordinal);
    }

    // generator-compat.test.ts 'reads version 2 back as its own' plus an enabled auto-reply.
    [Fact]
    [RustTest("sieve::vacation_audience_forward_match_bulwark")]
    public void Vacation_audience_forward_match_bulwark()
    {
        var meta = J("""
            {"version": 1, "rules": [],
             "vacation": {"isEnabled": true, "subject": "Away", "textBody": "Back \"soon\""},
             "includeVacation": true,
             "vacationForward": {"enabled": true, "to": "kollege@example.com", "keepCopy": true,
               "activeFrom": "2026-10-05T06:00:00.000Z", "activeUntil": "2026-10-16T16:00:00.000Z"},
             "vacationAudience": {"only": "external", "domains": ["example.com"]}}
            """);
        var got = SieveScript.GenerateMeta(meta, string.Empty, []);
        const string MetaJson = """{"version":2,"rules":[],"vacation":{"isEnabled":true,"subject":"Away","textBody":"Back \"soon\""},"includeVacation":true,"vacationForward":{"enabled":true,"to":"kollege@example.com","keepCopy":true,"activeFrom":"2026-10-05T06:00:00.000Z","activeUntil":"2026-10-16T16:00:00.000Z"},"vacationAudience":{"only":"external","domains":["example.com"]}}""";
        var want = "/* @metadata:begin\n" + MetaJson + "\n@metadata:end */\n\n"
            + "require [\"copy\", \"date\", \"envelope\", \"include\", \"relational\", \"vacation\"];\n\n"
            + "# Vacation auto-reply\n"
            + "if not envelope :domain :is \"from\" [\"example.com\"] {\n    include :personal :optional \"vacation\";\n}\n\n"
            + "# Vacation auto-reply\n"
            + "vacation :subject \"Away\" \"Back \\\"soon\\\"\";\n\n"
            + "# Vacation forwarding\n"
            + "if allof(" + FromT + ", " + UntilT + ") {\n    redirect :copy \"kollege@example.com\";\n}\n";
        Assert.Equal(want, got);
        var p = SieveScript.Parse(got);
        Assert.False(p.Locked);
        var rules = Rules(p.Metadata);
        Assert.Equal(got, SieveScript.GenerateMeta(SieveScript.WithRules(p.Metadata, rules), p.External, []));

        // adding a rule keeps the vacation extras
        rules.Add(SieveScript.Rule("N", "from", "contains", "x", "move", "F", false));
        var g = SieveScript.GenerateMeta(SieveScript.WithRules(p.Metadata, rules), p.External, []);
        Assert.Contains("\"vacationAudience\"", g, StringComparison.Ordinal);
        Assert.Contains("# Vacation forwarding", g, StringComparison.Ordinal);
        Assert.Contains("# Rule: N", g, StringComparison.Ordinal);

        // forward without copy, no period, spam guard: stop after redirect
        string[] ext = ["spamtestplus", "relational"];
        g = SieveScript.GenerateMeta(J("""{"version": 1, "rules": [], "vacationForward": {"enabled": true, "to": "a@b.example", "keepCopy": false}}"""), string.Empty, ext);
        Assert.Contains("""require ["comparator-i;ascii-numeric", "relational", "spamtestplus"];""", g, StringComparison.Ordinal);
        Assert.Contains("# Vacation forwarding\nif not spamtest :percent :value \"ge\" :comparator \"i;ascii-numeric\" \"50\" {\n    redirect \"a@b.example\";\n    stop;\n}\n", g, StringComparison.Ordinal);

        // a forward that is off keeps version 1 and writes no block
        g = SieveScript.GenerateMeta(J("""{"version": 1, "rules": [], "vacationForward": {"enabled": false, "to": "a@b.example", "keepCopy": false}}"""), string.Empty, []);
        Assert.Contains("{\"version\":1,", g, StringComparison.Ordinal);
        Assert.DoesNotContain("# Vacation forwarding", g, StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("sieve::unknown_or_bad_extras_stay_locked")]
    public void Unknown_or_bad_extras_stay_locked()
    {
        static string S(string metaJson) => "/* @metadata:begin\n" + metaJson + "\n@metadata:end */\n";
        Assert.True(SieveScript.Parse(S("""{"version": 3, "rules": []}""")).Locked);
        Assert.True(SieveScript.Parse(S("""{"version": 1, "rules": [], "other": 1}""")).Locked);
        Assert.True(SieveScript.Parse(S("""{"version": 2, "rules": [{"activeFrom": "soon"}]}""")).Locked);
        Assert.True(SieveScript.Parse(S("""{"version": 2, "rules": [], "vacationAudience": {"only": "x", "domains": []}}""")).Locked);
        Assert.False(SieveScript.Parse(S("""{"version": 2, "rules": [], "vacationAudience": {"only": "internal", "domains": ["a.de"]}}""")).Locked);
    }

    [Fact]
    [RustTest("sieve::form_days_become_utc_boundaries")]
    public void Form_days_become_utc_boundaries()
    {
        var f = SieveScript.DayBoundary("2026-10-05", false).Value!;
        Assert.NotNull(SieveScript.PeriodBoundaryOf(f));
        Assert.EndsWith(".000Z", f, StringComparison.Ordinal);
        Assert.EndsWith(":00.000Z", SieveScript.DayBoundary("2026-10-05", true).Value!, StringComparison.Ordinal);
        var empty = SieveScript.DayBoundary(string.Empty, false);
        Assert.True(empty.IsOk);
        Assert.Null(empty.Value);
        Assert.False(SieveScript.DayBoundary("5.10.2026", false).IsOk);
    }
}
