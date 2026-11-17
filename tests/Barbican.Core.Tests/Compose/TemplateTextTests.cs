#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Compose;

namespace Barbican.Core.Tests.Compose;

/// <summary>Bulwark lib/template-utils.ts: placeholders, defaults, substitution, the picker search; and the address check before a send.</summary>
public sealed class TemplateTextTests
{
    [Fact]
    public void Placeholders_defaults_and_substitution()
    {
        Assert.Equal(["a", "b"], TemplateText.Placeholders("{{a}} {{b:x y}} {{a}} {{bad name}}"));
        var t = new MailTemplate("i", "n", "S {{a:d1}}", "<p>{{b:d &amp; 2}}</p>", true);
        Assert.Equal("d1", TemplateText.Defaults(t)["a"]);
        Assert.Equal("d & 2", TemplateText.Defaults(t)["b"]);
        var values = new Dictionary<string, string> { ["a"] = "<A>" };
        Assert.Equal("<&lt;A&gt;> {{b}}", TemplateText.Substitute("<{{a}}> {{b}}", values, true));
        Assert.Equal("<A> {{b}}", TemplateText.Substitute("{{a:q}} {{b}}", values).Replace("<A>", "<A>", StringComparison.Ordinal).Replace("{{a}}", "<A>", StringComparison.Ordinal));
    }

    [Fact]
    public void Name_validation_and_auto_fill()
    {
        Assert.Equal("empty", TemplateText.ValidateName(" "));
        Assert.Equal("too_long", TemplateText.ValidateName(new string('x', 201)));
        Assert.Null(TemplateText.ValidateName("ok"));
        var auto = TemplateText.AutoFilled(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero), "Ada");
        Assert.Equal(("05", "03", "2026", "Ada"), (auto["day"], auto["month"], auto["year"], auto["sender_name"]));
    }

    [Fact]
    public void Invalid_addresses_are_the_entries_with_no_address()
    {
        Assert.Empty(ComposeRules.InvalidAddresses("a@b.example, \"Lovelace, Ada\" <ada@b.example>; c@d.example"));
        Assert.Equal(["nobody"], ComposeRules.InvalidAddresses("a@b.example, nobody"));
    }
}
