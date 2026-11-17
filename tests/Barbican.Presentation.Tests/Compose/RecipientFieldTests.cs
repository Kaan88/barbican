#pragma warning disable MA0002, MA0006 // asserts compare strings ordinally by nature
using Barbican.Core.Compose.Recipients;
using Barbican.Presentation.Compose;

namespace Barbican.Presentation.Tests.Compose;

/// <summary>The chip sequences of Rust chips.rs and Bulwark's RecipientInput: commit, Backspace, paste, autocomplete keys, groups (I-246..I-249). Addresses are fictional.</summary>
public sealed class RecipientFieldTests
{
    private static readonly SuggestionSources Sources = new()
    {
        Contacts = [("Anna Berg", "anna@x.example"), ("Anders Kim", "anders@y.example")],
        Groups = [("g1", "Team", [Recipient.Addr("A", "a@x.example"), Recipient.Addr("B", "b@x.example")])],
        Recent = [("", "sam@z.example")],
    };

    private static RecipientFieldViewModel Field() => new("To", () => Sources);

    private static void Type(RecipientFieldViewModel f, string text) => f.Edited(text);

    [Fact]
    public void A_typed_address_commits_on_comma_and_the_field_text_follows()
    {
        var f = Field();
        Type(f, "ann@x.example");
        Assert.True(f.Key(",", false));
        Assert.Equal("ann@x.example", f.Text);
        var chip = Assert.Single(f.Chips);
        Assert.Equal(ChipKind.Person, chip.Kind);
        Assert.Equal(string.Empty, f.Input);
    }

    [Fact]
    public void Text_that_is_no_address_becomes_an_invalid_chip_on_enter_and_is_never_dropped()
    {
        var f = Field();
        Type(f, "nobody");
        Assert.True(f.Key("Enter", false));
        Assert.Equal(ChipKind.Invalid, Assert.Single(f.Chips).Kind);
        Assert.Equal("nobody", f.Text);
    }

    [Fact]
    public void Backspace_on_an_empty_input_arms_the_last_chip_and_a_second_removes_it()
    {
        var f = Field();
        f.Seed("a@x.example, b@x.example");
        Assert.True(f.Key("Backspace", false));
        Assert.True(f.Chips[1].Armed);
        Assert.Equal(2, f.Chips.Count);
        Assert.True(f.Key("Backspace", false));
        Assert.Equal("a@x.example", f.Text);
    }

    [Fact]
    public void Another_key_disarms_the_chip()
    {
        var f = Field();
        f.Seed("a@x.example");
        _ = f.Key("Backspace", false);
        _ = f.Key("x", false);
        Assert.False(f.Chips[0].Armed);
    }

    [Fact]
    public void A_pasted_list_becomes_chips_at_once_and_what_is_no_address_stays_in_the_input()
    {
        var f = Field();
        f.Edited("a@x.example; b@y.example junk");
        Assert.Equal(["a@x.example", "b@y.example"], f.Recipients.Select(r => r.Email));
        Assert.Equal("junk", f.Input);
    }

    [Fact]
    public void Autocomplete_opens_on_typing_and_the_arrows_move_the_highlight()
    {
        var f = Field();
        Type(f, "an");
        Assert.True(f.IsOpen);
        Assert.Equal("Anna Berg", f.Suggestions[0].Name);
        Assert.Equal(0, f.Highlight);
        Assert.True(f.Key("ArrowDown", false));
        Assert.Equal(1, f.Highlight);
        Assert.True(f.Key("ArrowUp", false));
        Assert.Equal(0, f.Highlight);
        Assert.True(f.Key("Escape", false));
        Assert.False(f.IsOpen);
    }

    [Fact]
    public void Enter_takes_the_highlighted_suggestion_and_the_typed_text_goes()
    {
        var f = Field();
        Type(f, "an");
        _ = f.Key("ArrowDown", false);
        Assert.True(f.Key("Enter", false));
        Assert.Equal("Anders Kim <anders@y.example>", f.Text);
        Assert.Equal(string.Empty, f.Input);
        Assert.False(f.IsOpen);
    }

    [Fact]
    public void A_complete_typed_address_that_is_not_a_suggestion_is_added_as_typed()
    {
        var f = Field();
        Type(f, "new@q.example");
        Assert.True(f.Key("Enter", false));
        Assert.Equal("new@q.example", f.Text);
    }

    [Fact]
    public void Tab_takes_the_pick_and_lets_the_focus_move_on()
    {
        var f = Field();
        Type(f, "sam");
        Assert.False(f.Key("Tab", false));
        Assert.Equal("sam@z.example", f.Text);
    }

    [Fact]
    public void Blur_keeps_partial_text_while_suggestions_are_open_and_commits_a_whole_address()
    {
        var f = Field();
        Type(f, "an");
        f.Blur();
        Assert.Equal("an", f.Input);
        Assert.Empty(f.Chips);
        Type(f, "full@x.example");
        f.Blur();
        Assert.Equal("full@x.example", f.Text);
    }

    [Fact]
    public void A_group_suggestion_becomes_one_chip_and_a_double_click_opens_it_into_its_members()
    {
        var f = Field();
        Type(f, "team");
        Assert.True(f.Suggestions[0].IsGroup);
        f.Pick(0);
        var chip = Assert.Single(f.Chips);
        Assert.Equal(ChipKind.Group, chip.Kind);
        Assert.Equal(2, chip.Members);
        f.EditChip(0);
        Assert.Equal(["a@x.example", "b@x.example"], f.Recipients.Select(r => r.Email));
    }

    [Fact]
    public void Suggestions_leave_out_what_is_already_on_the_field()
    {
        var f = Field();
        f.Seed("anna@x.example");
        Type(f, "an");
        Assert.DoesNotContain(f.Suggestions, s => s.Detail == "anna@x.example");
    }

    [Fact]
    public void Editing_a_person_chip_puts_it_back_into_the_input()
    {
        var f = Field();
        f.Seed("Ann <ann@x.example>, b@x.example");
        f.EditChip(0);
        Assert.Equal("Ann <ann@x.example>", f.Input);
        Assert.Equal("b@x.example", f.Text);
    }

    [Fact]
    public void An_international_domain_shows_in_unicode_and_a_broken_address_is_marked()
    {
        var f = Field();
        f.Seed("jo@xn--bcher-kva.example, a@@b");
        Assert.Equal("jo@bücher.example", f.Chips[0].Label);
        Assert.Equal(ChipKind.Invalid, f.Chips[1].Kind);
    }

    [Fact]
    public void A_change_by_the_user_raises_the_event_and_a_seed_does_not()
    {
        var f = Field();
        var n = 0;
        f.TextChanged += (_, _) => n++;
        f.Seed("a@x.example");
        Assert.Equal(0, n);
        f.Remove(0);
        Assert.Equal(1, n);
    }
}
