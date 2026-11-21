// The .ics import card's alerts question (G-004, D-M4-09): the prompt appears only when the chosen events carry alerts, the default keeps them as Bulwark does, and "no" hands the importer false so the file's alerts are not stored. M4 stream cal-ui.

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Calendar.Editing;
using Barbican.Core.Common;
using Barbican.Presentation.Calendar.Editors.Dialogs;

namespace Barbican.Presentation.Tests.Calendar.Editors;

public sealed class ImportViewModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonObject Event(string title, bool alerts)
    {
        var e = new JsonObject { ["id"] = title, ["title"] = title, ["start"] = "2026-11-20T10:00:00" };
        if (alerts)
        {
            e["alerts"] = new JsonObject { ["a"] = new JsonObject { ["@type"] = "Alert", ["trigger"] = new JsonObject { ["@type"] = "OffsetTrigger", ["offset"] = "-PT15M" }, ["action"] = "display" } };
        }

        return e;
    }

    private static async Task<(ImportViewModel Vm, FakeImporter Importer)> OpenAsync(params JsonObject[] events)
    {
        var source = new FakeSource();
        var importer = new FakeImporter(events);
        var vm = new ImportViewModel(source, importer, new FakeRules(), new FakePicker("dinner.ics"));
        Assert.True(await vm.PickAsync(Ct));
        return (vm, importer);
    }

    [Fact]
    public async Task The_question_shows_for_a_file_with_alerts_and_keeps_them_by_default()
    {
        var (vm, importer) = await OpenAsync(Event("Dinner", alerts: true), Event("Walk", alerts: false));
        Assert.True(vm.ShowAlertsPrompt);
        Assert.True(vm.KeepAlerts);

        Assert.True(await vm.ImportAsync(Ct));
        var run = Assert.Single(importer.Runs);
        Assert.True(run.KeepAlerts);
        Assert.Equal(2, run.Events.Count);
        Assert.Equal("work", run.CalendarId);
        Assert.Equal("Imported 2 events.", vm.Status);
    }

    [Fact]
    public async Task Saying_no_strips_the_alerts_of_the_file()
    {
        var (vm, importer) = await OpenAsync(Event("Dinner", alerts: true));
        vm.KeepAlerts = false;
        Assert.True(await vm.ImportAsync(Ct));
        Assert.False(Assert.Single(importer.Runs).KeepAlerts);
    }

    [Fact]
    public async Task A_file_without_alerts_imports_the_same_whichever_way_the_box_sits()
    {
        var (vm, importer) = await OpenAsync(Event("Walk", alerts: false));
        Assert.False(vm.ShowAlertsPrompt);
        vm.KeepAlerts = false;
        Assert.True(await vm.ImportAsync(Ct));
        Assert.True(Assert.Single(importer.Runs).KeepAlerts);
    }

    [Fact]
    public async Task The_question_follows_the_selection_and_an_empty_one_is_refused()
    {
        var (vm, importer) = await OpenAsync(Event("Dinner", alerts: true), Event("Walk", alerts: false));
        vm.Items[0].Selected = false;
        Assert.False(vm.ShowAlertsPrompt);
        vm.SelectAll(false);
        Assert.False(await vm.ImportAsync(Ct));
        Assert.Equal("Select at least one event.", vm.Error);
        Assert.Empty(importer.Runs);
    }

    private sealed class FakePicker(string name) : ICalendarFilePicker
    {
        public Task<IcsFile?> OpenIcsAsync(CancellationToken ct) => Task.FromResult<IcsFile?>(new IcsFile(name, Encoding.UTF8.GetBytes("BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n")));

        public Task<string?> SaveTextAsync(string fileName, string text, CancellationToken ct) => Task.FromResult<string?>(null);
    }

    private sealed class FakeImporter(params JsonObject[] events) : IIcsImporter
    {
        public List<(string CalendarId, IReadOnlyList<JsonNode> Events, bool KeepAlerts)> Runs { get; } = [];

        public Task<Result<IReadOnlyList<JsonNode>>> ParseAsync(string accountKey, ReadOnlyMemory<byte> ics, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<JsonNode>>.Ok([.. events.Select(e => (JsonNode)e.DeepClone())]));

        public Task<Result<int>> ImportAsync(string accountKey, string calendarId, IReadOnlyList<JsonNode> events, bool keepAlerts, CancellationToken ct)
        {
            Runs.Add((calendarId, events, keepAlerts));
            return Task.FromResult(Result<int>.Ok(events.Count));
        }
    }
}
