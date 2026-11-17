// Ported from Rust src/bodycache.rs tests (:226-:345).
// The Rust cache held serde Values; this one holds EmailBody and ConversationThread, so the JSON literals become records.

using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.BodyCache;

public sealed class BodyCacheTests
{
    private static EmailBody Msg(string id, string body) => new()
    {
        Id = id,
        ThreadId = $"t{id}",
        BodyValues = new Dictionary<string, BodyValue>(StringComparer.Ordinal) { ["1"] = new BodyValue(body) },
    };

    private static EmailSummary Row(string id) => new() { Id = id };

    private static ConversationThread Thread(string id, params EmailSummary[] rows) => new(id, rows);

    [Fact]
    [RustTest("bodycache::get_put_and_lru_by_count")]
    public void Get_put_and_lru_by_count()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache(2, long.MaxValue);
        c.Put("a", "1", Msg("1", "x"));
        c.Put("a", "2", Msg("2", "y"));
        Assert.True(c.TryGet("a", "1", out _)); // 1 is now the most recent
        c.Put("a", "3", Msg("3", "z"));
        Assert.True(c.Contains("a", "1") && c.Contains("a", "3"));
        Assert.False(c.Contains("a", "2"), "least recently used goes first");
        Assert.Equal(2, c.Count);
    }

    [Fact]
    [RustTest("bodycache::per_account")]
    public void Per_account()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache();
        c.Put("a", "1", Msg("1", "x"));
        Assert.False(c.TryGet("b", "1", out _));
        Assert.True(c.TryGet("a", "1", out _));
    }

    [Fact]
    [RustTest("bodycache::bounded_by_bytes_keeps_newest")]
    public void Bounded_by_bytes_keeps_newest()
    {
        var big = new string('x', 10_000);
        var c = new Barbican.Core.Mail.Cache.BodyCache(100, 25_000);
        c.Put("a", "1", Msg("1", big));
        c.Put("a", "2", Msg("2", big));
        c.Put("a", "3", Msg("3", big));
        Assert.True(c.Bytes <= 25_000);
        Assert.True(c.Contains("a", "3") && !c.Contains("a", "1"));

        // one entry larger than the budget is still kept (it is the one on screen)
        c.Put("a", "4", Msg("4", new string('y', 40_000)));
        Assert.True(c.Contains("a", "4"));
        Assert.Equal(1, c.Count);
    }

    [Fact]
    [RustTest("bodycache::replacing_an_entry_keeps_the_byte_count")]
    public void Replacing_an_entry_keeps_the_byte_count()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache();
        c.Put("a", "1", Msg("1", "aaaa"));
        var b = c.Bytes;
        c.Put("a", "1", Msg("1", "bbbb"));
        Assert.Equal(b, c.Bytes);
        c.Remove("a", "1");
        Assert.Equal(0, c.Bytes);
        Assert.Equal(0, c.Count);
    }

    [Fact]
    [RustTest("bodycache::sync_patches_keywords_and_drops_removed_and_threads")]
    public void Sync_patches_keywords_and_drops_removed_and_threads()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache();
        c.Put("a", "1", Msg("1", "x"));
        c.Put("a", "2", Msg("2", "y"));
        c.PutThread("a", "t1", Thread("t1", Row("1")));
        c.PutThread("a", "t9", Thread("t9", Row("9")));
        var fresh = new Dictionary<string, EmailSummary>(StringComparer.Ordinal)
        {
            ["1"] = new EmailSummary
            {
                Id = "1",
                ThreadId = "t1",
                Keywords = Keywords.Of("$seen"),
                MailboxIds = new Dictionary<string, bool>(StringComparer.Ordinal) { ["m2"] = true },
            },
        };
        c.OnSync("a", fresh, ["2"], ["t9"]);
        Assert.True(c.TryGet("a", "1", out var m));
        Assert.True(m.Keywords.IsSet("$seen"));
        Assert.True(m.MailboxIds["m2"]);
        Assert.Equal("x", m.BodyValues["1"].Value); // the body stays
        Assert.False(c.Contains("a", "2"));
        Assert.False(c.TryGetThread("a", "t1", out _), "a changed message's thread is re-read");
        Assert.False(c.TryGetThread("a", "t9", out _));
    }

    [Fact]
    [RustTest("bodycache::local_actions_reach_bodies_and_conversations")]
    public void Local_actions_reach_bodies_and_conversations()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache();
        c.Put("a", "u:1", Msg("1", "x"));
        c.Put("b", "2", Msg("2", "y"));
        c.PutThread("a", "t1", Thread("t1", Row("1"), Row("3")));
        static string Real(string i) => i.StartsWith("u:", StringComparison.Ordinal) ? i[2..] : i;

        c.PatchLocal(["u:1"], Real, body => body with { Keywords = Keywords.Of("$seen") }, row => row with { Keywords = Keywords.Of("$seen") });
        Assert.True(c.TryGet("a", "u:1", out var body));
        Assert.True(body.Keywords.IsSet("$seen"));
        Assert.True(c.TryGetThread("a", "t1", out var t));
        Assert.True(t.Emails[0].Keywords.IsSet("$seen"), "the strip's row follows");
        Assert.False(t.Emails[1].Keywords.IsSet("$seen"));

        c.RemoveLocal(["3"], Real);
        Assert.False(c.TryGetThread("a", "t1", out _), "a conversation that lost a message is re-read");
        Assert.True(c.Contains("a", "u:1") && c.Contains("b", "2"));
        c.RemoveLocal(["2"], Real);
        Assert.False(c.Contains("b", "2"));
    }

    [Fact]
    [RustTest("bodycache::a_fetch_from_before_a_clear_is_not_stored")]
    public void A_fetch_from_before_a_clear_is_not_stored()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache();
        var g = c.Generation;
        c.Clear();
        c.Put("a", "1", Msg("1", "x"), g);
        c.PutThread("a", "t1", Thread("t1", Row("1")), g);
        Assert.False(c.Contains("a", "1"));
        Assert.False(c.TryGetThread("a", "t1", out _));
        c.Put("a", "1", Msg("1", "x"), c.Generation);
        Assert.True(c.Contains("a", "1"));
    }

    [Fact]
    [RustTest("bodycache::conversations_are_bounded_by_bytes")]
    public void Conversations_are_bounded_by_bytes()
    {
        var c = new Barbican.Core.Mail.Cache.BodyCache();
        var row = new EmailSummary { Id = "1", Preview = new string('p', 200_000) };
        for (var t = 0; t < 60; t++)
        {
            c.PutThread("a", $"t{t}", Thread($"t{t}", row));
        }

        Assert.True(c.ThreadBytes <= Barbican.Core.Mail.Cache.BodyCache.MaxThreadBytes);
        Assert.True(c.TryGetThread("a", "t59", out _));
        Assert.False(c.TryGetThread("a", "t0", out _));
    }

    [Fact]
    [RustTest("bodycache::weight_counts_headers")]
    public void Weight_counts_headers()
    {
        var m = Msg("1", "x");
        var w = Barbican.Core.Mail.Cache.BodyCache.Weight(m);
        m = m with { Headers = [new EmailHeader("Received", new string('r', 4000))] };
        Assert.True(Barbican.Core.Mail.Cache.BodyCache.Weight(m) >= w + 4000);
    }

    [Fact]
    [RustTest("bodycache::weight_counts_body_values")]
    public void Weight_counts_body_values()
    {
        Assert.True(Barbican.Core.Mail.Cache.BodyCache.Weight(Msg("1", new string('x', 1000))) >= 1000);
    }
}
