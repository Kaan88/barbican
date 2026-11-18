// M4 stream cal-ui acceptance: the task editor's field mapping (title, due date and time, priority, progress, repeat, alert, list) through TaskRules.Fields and the writer; Rust cal_extra.rs open_task and task_save.

using System.Text.Json.Nodes;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Tests.Calendar.Editors;

namespace Barbican.Presentation.Tests.Calendar.Tasks;

public sealed class TaskEditorViewModelTests
{
    private readonly FakeSource _source = new();
    private readonly FakeWriter _writer = new();

    [Fact]
    public async Task A_new_task_is_created_in_the_default_list_with_the_mapped_fields()
    {
        var e = new TaskEditorViewModel(_source, _writer);
        e.OpenNew(null, new DateOnly(2026, 11, 20));
        e.Title = " Pay ";
        e.HasTime = true;
        e.DueTime = "14:30";
        e.Priority = 1;
        e.Repeat = 2;
        e.Alert = 3;
        Assert.True(await e.SaveAsync(CancellationToken.None));
        var (list, d) = _writer.Creates[0];
        Assert.Equal("work", list);
        Assert.Equal("Task", (string?)d["@type"]);
        Assert.Equal("Pay", (string?)d["title"]);
        Assert.Equal("2026-11-20T14:30:00", (string?)d["due"]);
        Assert.Equal(1, (int?)d["priority"]);
        Assert.Equal("weekly", (string?)d["recurrenceRules"]![0]!["frequency"]);
        Assert.Equal("-PT15M", (string?)d["alerts"]!["alert1"]!["trigger"]!["offset"]);
        Assert.False(d.ContainsKey("timeZone") && d["timeZone"] is null);
        Assert.True(e.Closed.IsCompleted);
    }

    [Fact]
    public async Task An_empty_title_and_a_bad_date_are_refused_with_a_message()
    {
        var e = new TaskEditorViewModel(_source, _writer);
        e.OpenNew(null, null);
        Assert.False(await e.SaveAsync(CancellationToken.None));
        Assert.Equal("Give the task a title.", e.Error);
        e.Title = "x";
        e.DueDate = "20-11";
        Assert.False(await e.SaveAsync(CancellationToken.None));
        Assert.Equal("Use YYYY-MM-DD for the due date.", e.Error);
        Assert.Empty(_writer.Creates);
    }

    [Fact]
    public async Task An_existing_task_loads_and_saves_as_an_update_that_can_clear_the_due_date()
    {
        var task = JsonNode.Parse("{\"id\":\"t1\",\"@type\":\"Task\",\"title\":\"Call\",\"due\":\"2026-11-20T14:30:00\",\"showWithoutTime\":false,\"priority\":5,\"progress\":\"in-process\",\"calendarIds\":{\"home\":true},\"recurrenceRules\":[{\"frequency\":\"daily\"}],\"alerts\":{\"alert1\":{\"trigger\":{\"offset\":\"-PT1440M\"}}}}")!;
        var e = new TaskEditorViewModel(_source, _writer);
        e.OpenExisting(task);
        Assert.Equal("2026-11-20", e.DueDate);
        Assert.True(e.HasTime);
        Assert.Equal("14:30", e.DueTime);
        Assert.Equal(2, e.Priority);
        Assert.Equal(1, e.Progress);
        Assert.Equal("home", e.ListId);
        Assert.Equal(1, e.Repeat);
        Assert.Equal(6, e.Alert);
        e.DueDate = string.Empty;
        Assert.True(await e.SaveAsync(CancellationToken.None));
        var (id, patch) = _writer.Updates[0];
        Assert.Equal("t1", id);
        Assert.True(patch.ContainsKey("due") && patch["due"] is null);
        Assert.True((bool?)patch["calendarIds"]!["home"]);
    }

    [Fact]
    public async Task Delete_asks_and_destroys()
    {
        var host = new Barbican.Presentation.Calendar.Editors.CalendarEditorHost();
        var e = new TaskEditorViewModel(_source, _writer, host);
        e.OpenExisting(JsonNode.Parse("{\"id\":\"t1\",\"@type\":\"Task\",\"title\":\"Call\",\"calendarIds\":{\"work\":true}}")!);
        var asking = e.DeleteAsync(CancellationToken.None);
        Assert.Equal("delete-task", host.Question!.Id);
        host.Question.Complete("yes");
        Assert.True(await asking);
        Assert.Equal(["t1"], _writer.Destroys);
    }
}
