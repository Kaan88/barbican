using Barbican.Presentation.Editor;

namespace Barbican.Presentation.Tests.Editor;

/// <summary>G-075: the "@" popup trigger and the inserted label (Bulwark recipient-mention.tsx).</summary>
public sealed class MentionTriggerTests
{
    [Theory]
    [InlineData("@", 1, 0, "")]
    [InlineData("hi @ma", 6, 3, "ma")]
    [InlineData("hi @ma", 6, 3, "ma")]
    [InlineData("@ab cd", 3, 0, "ab")]
    public void An_at_sign_at_a_word_start_opens_the_list_with_the_query(string text, int caret, int start, string query)
    {
        var t = MentionTrigger.Find(text, caret);
        Assert.NotNull(t);
        Assert.Equal(start, t.Value.Start);
        Assert.Equal(query, t.Value.Query);
    }

    [Theory]
    [InlineData("info@mail.example", 17)]
    [InlineData("hello", 5)]
    [InlineData("@ab cd", 6)]
    [InlineData("", 0)]
    public void Addresses_and_plain_words_do_not_trigger(string text, int caret)
    {
        Assert.Null(MentionTrigger.Find(text, caret));
    }

    [Theory]
    [InlineData("Nagy János", "nj@mail.example", "Nagy")]
    [InlineData("", "alice@mail.example", "alice")]
    [InlineData("  Bob  ", "b@mail.example", "Bob")]
    [InlineData("", "nobody", "nobody")]
    public void The_label_is_the_first_name_or_the_local_part(string name, string email, string expected)
    {
        Assert.Equal(expected, MentionTrigger.Label(new MentionCandidate(name, email)));
    }
}
