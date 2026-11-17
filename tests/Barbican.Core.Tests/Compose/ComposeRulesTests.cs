using Barbican.Core.Compose;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Tests.Compose;

/// <summary>Rust src/compose.rs tests blank_drafts, delivered_to_headers and placeholders, ported (M3 stream send-pipeline).</summary>
public sealed class ComposeRulesTests
{
    [Fact]
    [RustTest("compose::blank_drafts")]
    public void Blank_drafts()
    {
        Assert.True(ComposeRules.IsBlank(["", " ", "", ""], "", [], 0));

        // only the signature Barbican inserted
        Assert.True(ComposeRules.IsBlank(["", "", "", ""], "\n\n-- \nAda Lovelace\nExample Ltd\n", ["Ada Lovelace\nExample Ltd"], 0));
        Assert.False(ComposeRules.IsBlank(["ada@example.org", "", "", ""], "", [], 0));
        Assert.False(ComposeRules.IsBlank(["", "", "", "Hi"], "", [], 0));
        Assert.False(ComposeRules.IsBlank(["", "", "", ""], "Hello\n-- \nAda", ["Ada"], 0));
        Assert.False(ComposeRules.IsBlank(["", "", "", ""], "", [], 1));
    }

    [Fact]
    [RustTest("compose::placeholders")]
    public void Placeholders()
    {
        Assert.Equal(["{{ name }}", "[TIME]", "[Your Name]", "TODO"], ComposeRules.FindPlaceholders("Hi {{ name }}", "Meet at [TIME] with [Your Name], TODO: agenda"));
        Assert.Equal(["[insert date here]", "〇〇"], ComposeRules.FindPlaceholders("", "see [insert date here] and 〇〇様"));

        // ordinary brackets, links and lowercase words are text
        Assert.Empty(ComposeRules.FindPlaceholders("", "[Yours truly] [Anlage 2] [link](http://x) [A] todo xxxl"));
        Assert.Empty(ComposeRules.FindPlaceholders("{{bad name}}", ""));
    }

    [Fact]
    [RustTest("compose::delivered_to_headers")]
    public void Delivered_to_headers()
    {
        var headers = new List<EmailHeader>
        {
            new("X-Original-To", " a@b.c"),
            new("Delivered-To", "Someone <d@e.f>"),
            new("delivered-to", "a@b.c"),
        };
        Assert.Equal(["d@e.f", "a@b.c"], ComposeRules.DeliveryAddresses(headers));
    }
}
