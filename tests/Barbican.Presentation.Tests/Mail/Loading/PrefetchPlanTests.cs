// Rust msgload.rs prefetch :320 (tag rust-final): two ahead in the direction of travel, three while moving one row at a time, one behind;
// the conversation neighbours of the open message. Owner body.

#pragma warning disable MA0002 // asserts compare strings ordinally by nature
using Barbican.Presentation.Mail;
using Barbican.Presentation.Mail.Loading;

namespace Barbican.Presentation.Tests.Mail.Loading;

public sealed class PrefetchPlanTests
{
    [Fact]
    public void First_open_goes_down_two_ahead_one_behind()
    {
        var (ids, travel) = PrefetchPlan.Around(5, 10, TravelState.Start);
        Assert.Equal([6, 7, 4], ids);
        Assert.Equal(new TravelState(5, false, false), travel);
    }

    [Fact]
    public void One_row_up_goes_up_three_ahead()
    {
        var (ids, travel) = PrefetchPlan.Around(4, 10, new TravelState(5, false, false));
        Assert.Equal([3, 2, 1, 5], ids);
        Assert.True(travel.Back);
        Assert.True(travel.Moving);
    }

    [Fact]
    public void A_jump_is_not_moving_and_the_same_row_keeps_its_direction()
    {
        var (ids, travel) = PrefetchPlan.Around(2, 10, new TravelState(8, false, false));
        Assert.Equal([1, 0, 3], ids);
        Assert.False(travel.Moving);
        var (again, kept) = PrefetchPlan.Around(2, 10, travel);
        Assert.Equal(ids, again);
        Assert.Equal(travel, kept);
    }

    [Fact]
    public void The_ends_of_the_list_clip()
    {
        Assert.Equal([1, 2], PrefetchPlan.Around(0, 10, TravelState.Start).Indices);
        Assert.Equal([8], PrefetchPlan.Around(9, 10, new TravelState(8, false, true)).Indices);
    }

    [Fact]
    public void Conversation_neighbours_are_the_messages_before_and_after()
    {
        ConversationItem Item(string id, bool current) => new(id, "s", "S", 0, "t", false, current);
        Assert.Equal(["a", "c"], PrefetchPlan.ConversationNeighbours([Item("a", false), Item("b", true), Item("c", false)]));
        Assert.Equal(["b"], PrefetchPlan.ConversationNeighbours([Item("a", true), Item("b", false)]));
        Assert.Empty(PrefetchPlan.ConversationNeighbours([Item("a", false)]));
    }
}
