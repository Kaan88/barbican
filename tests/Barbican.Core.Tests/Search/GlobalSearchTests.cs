// Ported from Rust src/gsearch.rs tests (:626-:791). Red until stream search implements GlobalSearch (M2 plan 6.5, T-102..T-114).
// Not verbatim: the Slint check `StyledText::from_markdown(&md).is_ok()` in `snippets` is dropped (D-235: the C# UI renders marked runs itself;
// the markdown string is still asserted exactly).

using System.Text.Json.Nodes;
using Barbican.Core.Search;

namespace Barbican.Core.Tests.Search;

public sealed class GlobalSearchTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    private static string? Roles(string role) => role switch { "trash" => "T", "junk" => "J", _ => null };

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(J(expected), actual), actual?.ToJsonString());

    private static SearchHit H(string title, string date) => new(SearchScope.Mail, title, title, string.Empty, date, string.Empty);

    [Fact]
    [RustTest("gsearch::mail_hit_prefers_open_folder_then_inbox")]
    public void Mail_hit_prefers_open_folder_then_inbox()
    {
        var m = J("""{"mailboxIds": {"zz": true, "inbox1": true, "cur": true}}""");
        Assert.Equal("cur", GlobalSearch.HitFolder(m, ["cur", "inbox1"]));
        Assert.Equal("inbox1", GlobalSearch.HitFolder(m, ["other", "inbox1"]));
        Assert.Equal("cur", GlobalSearch.HitFolder(m, [string.Empty, string.Empty]));
        Assert.Equal("a", GlobalSearch.HitFolder(J("""{"mailboxIds": {"b": true, "a": true}}"""), []));
        Assert.Equal(string.Empty, GlobalSearch.HitFolder(J("{}"), ["x"]));
    }

    [Fact]
    [RustTest("gsearch::parses_operators")]
    public void Parses_operators()
    {
        var p = GlobalSearch.Parse("from:alice subject:\"q3 report\" has:attachment is:unread budget after:2026-01-02 before:2026-13-01");
        Assert.Equal("alice", p.Mail.From);
        Assert.Equal("q3 report", p.Mail.Subject);
        Assert.True(p.Mail.HasAttachment);
        Assert.True(p.Mail.Unread);
        Assert.Equal("2026-01-02", p.Mail.After);

        // an impossible date stays text
        Assert.Null(p.Mail.Before);
        Assert.Equal("budget before:2026-13-01", p.Text);
        Assert.True(p.HasOperators);
        Assert.Equal(["budget", "before:2026-13-01"], p.Terms);
    }

    [Fact]
    [RustTest("gsearch::unknown_keys_and_phrases_stay_text")]
    public void Unknown_keys_and_phrases_stay_text()
    {
        var p = GlobalSearch.Parse("http://x.y \"exact words\" foo:bar");
        Assert.Equal("http://x.y \"exact words\" foo:bar", p.Text);
        Assert.False(p.HasOperators);

        p = GlobalSearch.Parse("in:files invoice");
        Assert.Equal(SearchScope.Files, p.Scope);
        Assert.Equal("invoice", p.Text);

        p = GlobalSearch.Parse("in:trash old");
        Assert.Equal(SearchScope.Mail, p.Scope);
        Assert.Equal("trash", p.MailboxRole);

        p = GlobalSearch.Parse("is:anything x");
        Assert.True(p.IncludeTrashAndJunk);

        p = GlobalSearch.Parse("subject:\"unfinished");
        Assert.Equal("unfinished", p.Mail.Subject);

        p = GlobalSearch.Parse("from: alone");
        Assert.Null(p.Mail.From);
        Assert.Equal("from: alone", p.Text);
    }

    [Fact]
    [RustTest("gsearch::scope_rewrite_and_chip_removal")]
    public void Scope_rewrite_and_chip_removal()
    {
        Assert.Equal("hello in:files", GlobalSearch.WithScope("hello in:mail", SearchScope.Files));
        Assert.Equal("hello in:trash", GlobalSearch.WithScope("hello in:trash", SearchScope.All));
        Assert.Equal("hello", GlobalSearch.WithScope("hello in:mail", SearchScope.All));
        Assert.Equal("budget to:b", GlobalSearch.WithoutOperator("from:a budget to:b", "from"));
    }

    [Fact]
    [RustTest("gsearch::split_text_like_bulwark")]
    public void Split_text_like_bulwark()
    {
        Assert.Equal(["annual report"], GlobalSearch.SplitSearchText("annual report"));
        Assert.Equal(["\"annual report\"", "2024", "\"draft\""], GlobalSearch.SplitSearchText("\"annual report\" 2024 \"draft\""));
        Assert.Equal(["project", "budg*"], GlobalSearch.SplitSearchText("project budg*"));
        Assert.Empty(GlobalSearch.SplitSearchText("*"));
        Assert.Equal(["quoted"], GlobalSearch.SplitSearchText("'quoted'"));
        var c = GlobalSearch.TextConditions("*", "text");
        Assert.Single(c);
        AssertJson("""{"text": "*"}""", c[0]);
    }

    [Fact]
    [RustTest("gsearch::mailbox_filter")]
    public void Mailbox_filter()
    {
        // plain text: one text condition in the folder
        AssertJson("""{"operator": "AND", "conditions": [{"inMailbox": "INBOX"}, {"text": "hello world"}]}""",
            GlobalSearch.MailboxSearchFilter("hello world", "INBOX", Roles));
        var f = GlobalSearch.MailboxSearchFilter("from:bob has:attachment is:starred", null, Roles);
        AssertJson("""{"operator": "AND", "conditions": [{"from": "bob"}, {"hasAttachment": true}, {"hasKeyword": "$flagged"}]}""", f);
        f = GlobalSearch.MailboxSearchFilter("in:trash x", "INBOX", Roles);
        AssertJson("""{"inMailbox": "T"}""", f["conditions"]![0]);
        var g = GlobalSearch.GlobalMailFilter(GlobalSearch.Parse("x"), Roles);
        AssertJson("""{"operator": "AND", "conditions": [{"text": "x"}, {"inMailboxOtherThan": ["T", "J"]}]}""", g);
        g = GlobalSearch.GlobalMailFilter(GlobalSearch.Parse("x is:anything"), Roles);
        AssertJson("""{"text": "x"}""", g);
        f = GlobalSearch.MailboxSearchFilter("before:2026-03-01", null, Roles);
        Assert.EndsWith("Z", (string)f["before"]!, StringComparison.Ordinal);
    }

    [Fact]
    [RustTest("gsearch::remote_query_needed")]
    public void Remote_query_needed()
    {
        Assert.True(GlobalSearch.HasRemoteQuery(GlobalSearch.Parse("x"), SearchScope.Contacts));
        Assert.False(GlobalSearch.HasRemoteQuery(GlobalSearch.Parse("is:unread"), SearchScope.Contacts));
        Assert.True(GlobalSearch.HasRemoteQuery(GlobalSearch.Parse("is:unread"), SearchScope.Mail));
        Assert.False(GlobalSearch.HasRemoteQuery(GlobalSearch.Parse(string.Empty), SearchScope.Mail));
    }

    [Fact]
    [RustTest("gsearch::chips_follow_operators")]
    public void Chips_follow_operators()
    {
        var c = GlobalSearch.Chips(GlobalSearch.Parse("from:ann is:unread has:attachment in:junk"));
        Assert.Equal(["from", "has", "is", "in"], c.Select(x => x.Key).ToArray());
        Assert.Equal("ann", c[0].Value);
    }

    [Fact]
    public void Has_the_words_and_size_operators_mirror_bulwarks_filter_panel()
    {
        // search-filter-panel.tsx:137 (body), :200-226 (KB bounds); search-utils.ts:200-245
        var p = GlobalSearch.Parse("body:\"q3 report\" larger:100 smaller:1.5");
        Assert.Equal("q3 report", p.Mail.Body);
        Assert.Equal("100", p.Mail.MinSizeKb);
        Assert.Equal("1.5", p.Mail.MaxSizeKb);
        Assert.Equal(string.Empty, p.Text);
        Assert.True(p.HasOperators);

        AssertJson(
            """{"operator": "AND", "conditions": [{"body": "q3 report"}, {"minSize": 102400}, {"maxSize": 1536}]}""",
            GlobalSearch.MailboxSearchFilter("body:\"q3 report\" larger:100 smaller:1.5", null, Roles));

        AssertJson("""{"operator": "AND", "conditions": [{"inMailbox": "INBOX"}, {"body": "invoice"}]}""",
            GlobalSearch.MailboxSearchFilter("body:invoice", "INBOX", Roles));
    }

    [Fact]
    public void Size_bounds_are_kilobytes_and_only_real_numbers_count()
    {
        // sizeFilterBytes (search-utils.ts:98-102): empty, non-numeric and non-positive set no condition
        Assert.Equal(1024L, GlobalSearch.SizeBytes("1"));
        Assert.Equal(1536L, GlobalSearch.SizeBytes("1.5"));
        Assert.Equal(2048L, GlobalSearch.SizeBytes(" 2 "));
        Assert.Null(GlobalSearch.SizeBytes(null));
        Assert.Null(GlobalSearch.SizeBytes(string.Empty));
        Assert.Null(GlobalSearch.SizeBytes("0"));
        Assert.Null(GlobalSearch.SizeBytes("-5"));
        Assert.Null(GlobalSearch.SizeBytes("abc"));

        // an invalid bound is not an operator: it stays free text and sets no condition (like an impossible date)
        var p = GlobalSearch.Parse("larger:0 smaller:abc");
        Assert.Null(p.Mail.MinSizeKb);
        Assert.Null(p.Mail.MaxSizeKb);
        Assert.Equal("larger:0 smaller:abc", p.Text);
        Assert.False(p.HasOperators);
        Assert.Empty(GlobalSearch.Chips(p));
    }

    [Fact]
    public void Size_chips_are_bulwarks_kb_chips()
    {
        // search-chips.tsx:65-70: label size_min / size_max, value "<kb> KB"
        var c = GlobalSearch.Chips(GlobalSearch.Parse("body:invoice larger:100 smaller:2048"));
        Assert.Equal(["body", "larger", "smaller"], c.Select(x => x.Key).ToArray());
        Assert.Equal(new SearchChip("body", "Body", "invoice"), c[0]);
        Assert.Equal(new SearchChip("larger", "Larger than (KB)", "100 KB"), c[1]);
        Assert.Equal(new SearchChip("smaller", "Smaller than (KB)", "2048 KB"), c[2]);

        // removing the chip takes the operator out and keeps the words
        Assert.Equal("invoice", GlobalSearch.WithoutOperator("invoice larger:100", "larger"));
    }

    [Fact]
    [RustTest("gsearch::snippets")]
    public void Snippets()
    {
        var s = GlobalSearch.SnippetSegments("Q3 <mark>budget</mark> &amp; plan &lt;b&gt;");
        Assert.Equal([new TextSegment("Q3 ", false), new TextSegment("budget", true), new TextSegment(" & plan <b>", false)], s);
        Assert.True(GlobalSearch.HasMarked(s));
        Assert.False(GlobalSearch.HasMarked(GlobalSearch.SnippetSegments("no marks here")));
        var md = GlobalSearch.SnippetMarkdown(s, "#ff0000");
        Assert.Equal("Q3 <font color=\"#ff0000\">**budget**</font> \\& plan \\<b\\>", md);

        // unexpected tags are text
        Assert.Equal([new TextSegment("<b>x</b>", false)], GlobalSearch.SnippetSegments("<b>x</b>"));
    }

    [Fact]
    [RustTest("gsearch::snippet_filter_loosens_terms")]
    public void Snippet_filter_loosens_terms()
    {
        var f = J("""{"operator": "AND", "conditions": [{"inMailbox": "a"}, {"text": "\"annual report\""}, {"subject": "budg* plan"}]}""");
        Assert.True(GlobalSearch.HasSnippetTerms(f));
        Assert.False(GlobalSearch.HasSnippetTerms(J("""{"inMailbox": "a"}""")));
        var s = GlobalSearch.SnippetFilter(f);
        Assert.Equal("annual report", (string)s["conditions"]![1]!["text"]!);
        Assert.Equal("budg plan", (string)s["conditions"]![2]!["subject"]!);
        Assert.Equal("a", (string)s["conditions"]![0]!["inMailbox"]!);
    }

    [Fact]
    [RustTest("gsearch::ranking")]
    public void Ranking()
    {
        var p = GlobalSearch.Parse("report");
        var ranked = GlobalSearch.Rank([H("the annual reports", "2026-01-01"), H("misc", "2026-05-01"), H("report", "2020-01-01"), H("reporting q3", "2021-01-01")], p);
        Assert.Equal(["report", "reporting q3", "the annual reports", "misc"], ranked.Select(x => x.Title).ToArray());

        // ties go to the newest
        ranked = GlobalSearch.Rank([H("misc a", "2026-01-01"), H("misc b", "2026-05-01")], GlobalSearch.Parse("zzz"));
        Assert.Equal("misc b", ranked[0].Title);
    }

    [Fact]
    [RustTest("gsearch::files_filter_skips_trash")]
    public void Files_filter_skips_trash()
    {
        IReadOnlyList<JsonNode> nodes =
        [
            J("""{"id": "t", "name": ".Trash", "parentId": null}"""),
            J("""{"id": "b", "name": "1~", "parentId": "t"}"""),
            J("""{"id": "x", "name": "report.pdf", "parentId": "b", "blobId": "B", "type": "application/pdf"}"""),
            J("""{"id": "d", "name": "Work", "parentId": null}"""),
            J("""{"id": "y", "name": "Report 2026.docx", "parentId": "d", "blobId": "C"}"""),
            J("""{"id": "z", "name": "Reports", "parentId": null}"""),
        ];
        var hits = GlobalSearch.FilterFiles(nodes, GlobalSearch.Parse("report"));
        Assert.Equal(["y", "z"], hits.Select(h => h.Id).ToArray());
        Assert.Equal("/Work", hits[0].Subtitle);
        Assert.Equal("d", hits[0].Place);

        // a folder opens itself
        Assert.Equal("z", hits[1].Place);

        // "folder" matches folders by kind
        Assert.Single(GlobalSearch.FilterFiles(nodes, GlobalSearch.Parse("folder work")));
    }

    [Fact]
    [RustTest("gsearch::terms_match_any_field")]
    public void Terms_match_any_field()
    {
        string[] t = ["ann", "acme"];
        Assert.True(GlobalSearch.MatchesTerms(t, ["Ann Lee", "ann@acme.example"]));
        Assert.False(GlobalSearch.MatchesTerms(t, ["Ann Lee", string.Empty]));
        Assert.False(GlobalSearch.MatchesTerms([], ["x"]));
    }
}
