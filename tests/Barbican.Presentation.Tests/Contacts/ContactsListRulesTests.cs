// Not Rust rows: B-CON-004 (sort by last name; Rust contacts.rs:83 sort_name), B-CON-027 (initials; Rust cal_editor.rs:69 initials), B-CON-030 (letter headers), I-352 (search). M5 stream con-ui.

using System.Text.Json.Nodes;
using Barbican.Presentation.Contacts;

namespace Barbican.Presentation.Tests.Contacts;

public sealed class ContactsListRulesTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    [Fact]
    [Red("con-ui")]
    public void Sort_names_follow_the_by_last_name_setting()
    {
        var components = J("""{"name": {"components": [{"kind": "given", "value": "Ada"}, {"kind": "surname", "value": "Lovelace"}, {"kind": "surname2", "value": "Byron"}], "full": "Ada Lovelace"}}""");
        Assert.Equal("Ada Lovelace", ContactsListRules.SortName(components, false));
        Assert.Equal("Lovelace Byron, Ada", ContactsListRules.SortName(components, true));
        var plain = J("""{"name": {"full": "Ada King Lovelace"}}""");
        Assert.Equal("Lovelace, Ada King", ContactsListRules.SortName(plain, true));
        Assert.Equal("Cher", ContactsListRules.SortName(J("""{"name": {"full": "Cher"}}"""), true));
        Assert.Equal("ada@x.example", ContactsListRules.FullName(J("""{"emails": {"e": {"address": "ada@x.example"}}}""")));
    }

    [Fact]
    [Red("con-ui")]
    public void Initials_and_letter_headers()
    {
        Assert.Equal("AL", ContactsListRules.Initials("Ada Lovelace", string.Empty));
        Assert.Equal("CH", ContactsListRules.Initials("Cher", string.Empty));
        Assert.Equal("JD", ContactsListRules.Initials(string.Empty, "jane.doe@x.example"));
        Assert.Equal("?", ContactsListRules.Initials(string.Empty, string.Empty));
        Assert.Equal("E", ContactsListRules.LetterOf("Émile Zola"));
        Assert.Equal("#", ContactsListRules.LetterOf("42 Club"));
    }

    [Fact]
    [Red("con-ui")]
    public void Search_matches_every_word_across_name_email_phone_and_organization()
    {
        var card = J("""{"name": {"full": "Anna Müller"}, "emails": {"e": {"address": "anna@acme.example"}}, "phones": {"p": {"number": "+49 30 5550000"}}, "organizations": {"o": {"name": "ACME Ltd"}}}""");
        Assert.True(ContactsListRules.Matches(card, "muller"));
        Assert.True(ContactsListRules.Matches(card, "anna acme"));
        Assert.True(ContactsListRules.Matches(card, "5550000"));
        Assert.False(ContactsListRules.Matches(card, "anna bob"));
        Assert.True(ContactsListRules.Matches(card, string.Empty));
    }
}
