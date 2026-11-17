// Ported from Rust src/dnd.rs tests (:301, :310, :335). Red until stream actions implements EventMove (M2 plan 2.1: T-085 and T-089 are pure Core functions that M4 consumes).

using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Core.DragDrop;

namespace Barbican.Core.Tests.DragDrop;

public sealed class EventMoveTests
{
    private static JsonNode J(string json) => JsonNode.Parse(json)!;

    [Fact]
    [RustTest("dnd::tiny_drag_snaps_back_to_no_change")]
    public void Tiny_drag_snaps_back_to_no_change()
    {
        var s = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Unspecified);
        var d = TimeSpan.FromMinutes(60);
        Assert.Equal((s, d), EventMove.Moved(s, d, 0, 5, false));
        Assert.Equal((s, d), EventMove.Moved(s, d, 0, -7, true));
        Assert.NotEqual((s, d), EventMove.Moved(s, d, 0, 8, false));
    }

    [Fact]
    [RustTest("dnd::move_notifies_attendees_only_when_there_are_some")]
    public void Move_notifies_attendees_only_when_there_are_some()
    {
        var p = J("""{"start": "2026-01-05T10:00:00"}""");
        var a = EventMove.UpdateArgs("e1", p.DeepClone(), J("""{"participants": {"x": {"email": "a@b"}}}"""));
        Assert.True((bool)a["sendSchedulingMessages"]!);
        Assert.True(JsonNode.DeepEquals(p, a["update"]!["e1"]));

        a = EventMove.UpdateArgs("e1", p.DeepClone(), J("""{"participants": {}}"""));
        Assert.False(a.ContainsKey("sendSchedulingMessages"));
        Assert.False(EventMove.UpdateArgs("e1", p.DeepClone(), J("{}")).ContainsKey("sendSchedulingMessages"));
    }

    [Fact]
    [RustTest("dnd::event_moves_snap_to_quarters")]
    public void Event_moves_snap_to_quarters()
    {
        var s = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Unspecified);
        var d = TimeSpan.FromMinutes(60);
        var (ns, nd) = EventMove.Moved(s, d, 1, 37, false);
        Assert.Equal("2026-11-17 09:30", ns.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Assert.Equal(d, nd);

        (ns, nd) = EventMove.Moved(s, d, 0, -52, false);
        Assert.Equal("08:15", ns.ToString("HH:mm", CultureInfo.InvariantCulture));
        Assert.Equal(d, nd);

        (_, nd) = EventMove.Moved(s, d, 0, 22, true);
        Assert.Equal(75, nd.TotalMinutes);

        (_, nd) = EventMove.Moved(s, d, 0, -200, true);
        Assert.Equal(EventMove.StepMinutes, nd.TotalMinutes);
        Assert.Equal("PT1H30M", EventMove.IsoMinutes(90));
        Assert.Equal("PT2H", EventMove.IsoMinutes(120));
        Assert.Equal("PT15M", EventMove.IsoMinutes(15));
    }
}
