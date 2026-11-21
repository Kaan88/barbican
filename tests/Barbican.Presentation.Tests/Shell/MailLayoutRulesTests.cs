// SH-12: Bulwark 1.21.2 stores/settings-store.ts:448-466 (effectiveMailLayout), hooks/use-effective-mail-layout.ts:14-19 (drawnMailLayout), mail-app.tsx:5794-5844 (which pane shows).

using Barbican.Presentation.Shell;

namespace Barbican.Presentation.Tests.Shell;

public sealed class MailLayoutRulesTests
{
    [Theory]
    [InlineData("split", MailLayoutMode.Split)]
    [InlineData("focus", MailLayoutMode.Focus)]
    [InlineData("horizontal", MailLayoutMode.Horizontal)]
    [InlineData("zzz", MailLayoutMode.Split)]
    [InlineData(null, MailLayoutMode.Split)]
    public void The_saved_value_names_the_layout_and_unknown_is_split(string? value, MailLayoutMode expected)
    {
        Assert.Equal(expected, MailLayoutRules.Parse(value));
        Assert.Equal(expected, MailLayoutRules.Parse(MailLayoutRules.Key(expected)));
    }

    [Theory]
    [InlineData(MailLayoutMode.Split, false, false, MailLayoutMode.Split)]
    [InlineData(MailLayoutMode.Horizontal, false, false, MailLayoutMode.Horizontal)]
    [InlineData(MailLayoutMode.Split, true, false, MailLayoutMode.Focus)]
    [InlineData(MailLayoutMode.Horizontal, true, false, MailLayoutMode.Focus)]
    [InlineData(MailLayoutMode.Split, false, true, MailLayoutMode.Focus)]
    [InlineData(MailLayoutMode.Focus, false, false, MailLayoutMode.Focus)]
    public void Mountain_View_and_Simple_mode_are_the_focused_layout(MailLayoutMode saved, bool mountainView, bool simple, MailLayoutMode expected) =>
        Assert.Equal(expected, MailLayoutRules.Effective(saved, mountainView, simple));

    [Fact]
    public void Split_keeps_the_list_and_the_reading_pane_side_by_side_open_or_not()
    {
        foreach (var open in new[] { false, true })
        {
            var plan = MailLayoutRules.Plan(MailLayoutMode.Split, open);
            Assert.True(plan.ListShown);
            Assert.True(plan.ReaderShown);
            Assert.False(plan.Stacked);
            Assert.True(plan.HasSplitter);
        }
    }

    [Fact]
    public void Focus_shows_the_list_full_width_then_the_message_in_its_place()
    {
        var closed = MailLayoutRules.Plan(MailLayoutMode.Focus, false);
        Assert.True(closed.ListShown);
        Assert.False(closed.ReaderShown);
        Assert.True(closed.ListFillsPane);
        Assert.False(closed.HasSplitter);
        var open = MailLayoutRules.Plan(MailLayoutMode.Focus, true);
        Assert.False(open.ListShown);
        Assert.True(open.ReaderShown);
        Assert.False(open.HasSplitter);
    }

    [Fact]
    public void Horizontal_stacks_the_reading_pane_under_the_list_and_fills_with_the_list_when_nothing_is_open()
    {
        var open = MailLayoutRules.Plan(MailLayoutMode.Horizontal, true);
        Assert.True(open.ListShown && open.ReaderShown && open.Stacked && open.HasSplitter && open.ListFillsPane);
        var closed = MailLayoutRules.Plan(MailLayoutMode.Horizontal, false);
        Assert.True(closed.ListShown && closed.Stacked && closed.ListFillsPane);
        Assert.False(closed.ReaderShown);
        Assert.False(closed.HasSplitter);
    }

    [Fact]
    public void A_split_layout_too_narrow_for_two_panes_behaves_as_focus_but_horizontal_does_not()
    {
        Assert.Equal(MailLayoutRules.Plan(MailLayoutMode.Focus, true), MailLayoutRules.Plan(MailLayoutMode.Split, true, tooNarrow: true));
        Assert.Equal(MailLayoutRules.Plan(MailLayoutMode.Horizontal, true), MailLayoutRules.Plan(MailLayoutMode.Horizontal, true, tooNarrow: true));
    }

    [Theory]
    [InlineData(0, 900, 320)] // none saved: the default
    [InlineData(400, 900, 400)]
    [InlineData(100, 900, 160)] // below the minimum
    [InlineData(700, 600, 360)] // the reading pane keeps 240
    [InlineData(700, 0, 700)] // not laid out yet: no clamp to the height
    [InlineData(700, 300, 160)] // never below the list minimum
    public void The_stacked_list_height_is_saved_or_default_and_leaves_the_reading_pane_its_minimum(double saved, double available, double expected) =>
        Assert.Equal(expected, MailLayoutRules.StackedListHeight(saved, available, 160, 240, 320));
}
