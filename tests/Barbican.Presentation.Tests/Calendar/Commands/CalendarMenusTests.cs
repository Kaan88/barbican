// M4 stream cal-ui acceptance: every menu's item list equals spec/menus.md for each state (MENU-EVENT with the RSVP rows, MENU-SLOT, MENU-TASK with its Due date flyout, MENU-CAL with its colour flyout); I-326..I-330, I-344.

using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Commands;
using Barbican.Presentation.Commands;
using Barbican.Presentation.Menus;
using Barbican.Presentation.Tests.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Commands;

public sealed class CalendarMenusTests
{
    private readonly FakeSource _source = new();

    private static MenuContext Ctx(string menu, string subject) => new(menu, [], null, null, null, null, CommandSurface.ContextMenu) { Subject = subject };

    private static string[] Labels(IEnumerable<MenuItemSpec> items) =>
        [.. items.Select(i => i.Kind switch { MenuItemKind.Separator => "—", MenuItemKind.Header => "# " + i.Label, MenuItemKind.Submenu => i.Label + " ›", _ => i.Label })];

    private IReadOnlyList<MenuItemSpec> Event(string json)
    {
        _source.Events = [JsonNode.Parse(json)!];
        return CalendarMenus.Event(Ctx(MenuIds.Event, "e1"), _source);
    }

    [Fact]
    public void An_event_the_user_organises_has_edit_duplicate_export_copy_print_and_delete()
    {
        var items = Event("{\"id\":\"e1\",\"title\":\"Lunch\",\"calendarIds\":{\"work\":true}}");
        Assert.Equal(["Edit", "Duplicate", "—", "Export as .ics", "Copy title", "Print", "—", "Delete"], Labels(items));
        Assert.True(items[^1].Danger);
        Assert.Equal("Del", items[^1].Hint);
        Assert.Equal(CalendarCommandIds.EventDelete, items[^1].CommandId);
    }

    [Fact]
    public void A_guest_sees_the_answers_that_are_not_the_current_one_first()
    {
        const string guest = "{\"id\":\"e1\",\"title\":\"Board\",\"calendarIds\":{\"work\":true},\"participants\":{\"p1\":{\"email\":\"ann@y.example\",\"roles\":{\"owner\":true}},\"p2\":{\"email\":\"me@x.example\",\"participationStatus\":\"STATUS\"}}}";
        Assert.Equal(["Accept", "Maybe", "Decline", "—"], Labels(Event(guest.Replace("STATUS", "needs-action", StringComparison.Ordinal))).Take(4).ToArray());
        Assert.Equal(["Maybe", "Decline", "—", "Edit"], Labels(Event(guest.Replace("STATUS", "accepted", StringComparison.Ordinal))).Take(4).ToArray());
        Assert.Equal(["Accept", "Maybe", "—", "Edit"], Labels(Event(guest.Replace("STATUS", "declined", StringComparison.Ordinal))).Take(4).ToArray());
        var rsvp = Event(guest.Replace("STATUS", "needs-action", StringComparison.Ordinal))[0];
        Assert.Equal(CalendarArg.Join("accepted", "e1"), rsvp.Argument);
    }

    [Fact]
    public void An_event_with_a_web_address_as_location_offers_the_meeting_link()
    {
        var labels = Labels(Event("{\"id\":\"e1\",\"title\":\"Sync\",\"calendarIds\":{\"work\":true},\"locations\":{\"l1\":{\"name\":\"https://meet.example/a\"}}}"));
        Assert.Contains("Join meeting", labels, StringComparer.Ordinal);
        Assert.Contains("Copy meeting link", labels, StringComparer.Ordinal);
    }

    [Fact]
    public void An_event_of_a_feed_cannot_be_duplicated_or_deleted()
    {
        var items = Event("{\"id\":\"e1\",\"title\":\"Holiday\",\"calendarIds\":{\"feed\":true}}");
        Assert.False(items.First(i => string.Equals(i.Label, "Duplicate", StringComparison.Ordinal)).Enabled);
        Assert.False(items[^1].Enabled);
    }

    [Fact]
    public void An_empty_slot_offers_new_event_all_day_task_and_today()
    {
        var items = CalendarMenus.Slot(Ctx(MenuIds.Slot, "2026-11-20T09:00"));
        Assert.Equal(["New event", "New all-day event", "New task", "—", "Go to today"], Labels(items));
        Assert.Equal("2026-11-20T09:00", items[0].Argument);
    }

    [Fact]
    public void A_task_has_complete_due_date_flyout_and_delete()
    {
        _source.Events = [JsonNode.Parse("{\"id\":\"t1\",\"@type\":\"Task\",\"title\":\"x\",\"progress\":\"needs-action\",\"calendarIds\":{\"work\":true}}")!];
        Assert.Equal(["Mark complete", "Due date ›", "—", "Delete"], Labels(CalendarMenus.Task(Ctx(MenuIds.Task, "t1"), _source)));
        Assert.Equal(["Today", "Tomorrow", "Next week"], Labels(CalendarMenus.TaskDue(Ctx(MenuIds.TaskDue, "t1"), _source)));

        _source.Events = [JsonNode.Parse("{\"id\":\"t1\",\"@type\":\"Task\",\"title\":\"x\",\"progress\":\"completed\",\"due\":\"2026-11-20T00:00:00\",\"calendarIds\":{\"work\":true}}")!];
        Assert.Equal("Mark incomplete", Labels(CalendarMenus.Task(Ctx(MenuIds.Task, "t1"), _source))[0]);
        Assert.Equal(["Today", "Tomorrow", "Next week", "No due date"], Labels(CalendarMenus.TaskDue(Ctx(MenuIds.TaskDue, "t1"), _source)));
    }

    [Fact]
    public void A_calendar_has_the_spec_items_and_the_default_one_cannot_be_deleted()
    {
        var work = CalendarMenus.Calendar(Ctx(MenuIds.Calendar, "work"), _source);
        Assert.Equal(["# Work", "New event in this calendar", "Hide calendar", "Rename", "Colour ›", "Set as default", "—", "Delete calendar"], Labels(work));
        Assert.False(work[5].Enabled);
        Assert.False(work[^1].Enabled);
        var home = CalendarMenus.Calendar(Ctx(MenuIds.Calendar, "home"), _source);
        Assert.True(home[5].Enabled);
        Assert.True(home[^1].Enabled);
        Assert.True(home[^1].Danger);

        _source.Calendars = [JsonNode.Parse("{\"id\":\"home\",\"name\":\"Home\",\"color\":\"#22c55e\",\"isVisible\":false}")!];
        Assert.Equal("Show calendar", CalendarMenus.Calendar(Ctx(MenuIds.Calendar, "home"), _source)[2].Label);
    }

    [Fact]
    public void The_colour_flyout_lists_the_fourteen_colours_with_the_current_one_checked()
    {
        var items = CalendarMenus.CalendarColour(Ctx(MenuIds.CalendarColour, "work"), _source);
        Assert.Equal(14, items.Count);
        Assert.Equal(["Blue"], items.Where(i => i.Checked == true).Select(i => i.Label).ToArray());
        Assert.Equal(CalendarArg.Join("#ef4444", "work"), items[1].Argument);
    }

    [Fact]
    public void An_unknown_subject_has_no_items()
    {
        Assert.Empty(CalendarMenus.Event(Ctx(MenuIds.Event, "nope"), _source));
        Assert.Empty(CalendarMenus.Task(Ctx(MenuIds.Task, "nope"), _source));
        Assert.Empty(CalendarMenus.Calendar(Ctx(MenuIds.Calendar, "nope"), _source));
    }

    [Fact]
    public void The_providers_are_registered_under_the_menu_ids()
    {
        var ids = CalendarMenus.Create(_source).Select(p => p.MenuId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(CalendarMenus.Count, ids.Count);
        Assert.Contains(MenuIds.Event, ids);
        Assert.Contains(MenuIds.Slot, ids);
        Assert.Contains(MenuIds.Task, ids);
        Assert.Contains(MenuIds.Calendar, ids);
    }
}
