#if AUTOMATION
// Mirrors: spec/automation-protocol.md (feature commands are IAutomationCommand registrations of the feature, M1-plan section 4.4); M4 plan section 6.3 (cal-open, cal-view, cal-dump, cal-drag, cal-click through the same entry as the pointer handlers; hidden automation only, ADR 0007).
// Every command runs on the UI thread and answers one line. Nothing here injects pointer or key input: a drag is CalendarGestures (what the pointer handlers call after their geometry), a click is CalendarUi (what the handlers call).
//   cal-open [day|week|month|agenda|tasks] [yyyy-MM-dd]   opens the Calendar app (and shows a view and a date)
//   cal-view day|week|month|agenda|tasks                  switches the view
//   cal-nav today|prev|next|yyyy-MM-dd                    pages
//   cal-dump [state|occurrences|layout|agenda|month]      the state as one line of JSON
//   cal-drag move ID yyyy-MM-ddTHH:mm | resize ID yyyy-MM-ddTHH:mm | create START END [allday] | fail-next | writes
//   cal-click event|open|slot|day|more|toggle|search|mini|allday|print|menu|select|retry|hover ...
//   cal-bulk TASKS EVENTS                                 adds sample items (the L-027 probe), cal-perf VIEW times a view switch

using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Barbican.Automation;
using Barbican.Presentation.Calendar.Grid;
using Barbican.Presentation.Shell;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable MA0051 // automation command tables read as one list of commands; sample data is one block
#pragma warning disable MA0048 // the command record sits beside its table

namespace Barbican.App.Views.Calendar.Automation;

/// <summary>One calendar automation command.</summary>
public sealed class CalendarCommand(string name, Func<string[], CancellationToken, Task<string>> run) : IAutomationCommand
{
    public string Name => name;

    public async Task<string> RunAsync(string args, CancellationToken cancellationToken)
    {
        var words = (args ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        try
        {
            return await run(words, cancellationToken).ConfigureAwait(true);
        }
        catch (Barbican.Core.Common.PortPendingException e)
        {
            return "error " + e.Message;
        }
    }

    /// <summary>The five commands of the brief and their helpers.</summary>
    public static IEnumerable<IAutomationCommand> Create(IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(sp);
        var vm = sp.GetRequiredService<CalendarViewModel>();
        var shell = sp.GetRequiredService<ShellViewModel>();
        var gestures = sp.GetRequiredService<CalendarGestures>();
        var sample = sp.GetService<ICalendarSource>() as SampleCalendarSource;

        yield return new CalendarCommand("cal-open", (w, _) =>
        {
            if (!shell.TryActivate(AppId.Calendar))
            {
                return Task.FromResult("error the Calendar app is not available (the login has no calendars capability)");
            }

            foreach (var word in w)
            {
                if (TryView(word, out var view))
                {
                    vm.SetView(view);
                }
                else if (TryDate(word, out var day))
                {
                    vm.ShowDay(day);
                }
            }

            return Task.FromResult(Summary(vm));
        });

        yield return new CalendarCommand("cal-view", (w, _) =>
        {
            if (w.Length == 0 || !TryView(w[0], out var view))
            {
                return Task.FromResult("error cal-view day|week|month|agenda|tasks");
            }

            vm.SetView(view);
            return Task.FromResult(Summary(vm));
        });

        yield return new CalendarCommand("cal-nav", (w, _) =>
        {
            switch (w.FirstOrDefault())
            {
                case "today": vm.Today(); break;
                case "prev": vm.GoPrevious(); break;
                case "next": vm.GoNext(); break;
                case { } d when TryDate(d, out var day): vm.ShowDay(day); break;
                default: return Task.FromResult("error cal-nav today|prev|next|yyyy-MM-dd");
            }

            return Task.FromResult(Summary(vm));
        });

        yield return new CalendarCommand("cal-dump", (w, _) => Task.FromResult(Dump(vm, w.FirstOrDefault() ?? "state")));

        yield return new CalendarCommand("cal-drag", async (w, ct) =>
        {
            switch (w.FirstOrDefault())
            {
                case "move" when w.Length >= 3 && TryTime(w[2], out var start):
                {
                    var r = await gestures.MoveAsync(w[1], start, ct).ConfigureAwait(true);
                    return r.IsOk ? "ok" : "error " + r.Error!.Message;
                }

                case "resize" when w.Length >= 3 && TryTime(w[2], out var end):
                {
                    var r = await gestures.ResizeAsync(w[1], end, ct).ConfigureAwait(true);
                    return r.IsOk ? "ok" : "error " + r.Error!.Message;
                }

                case "create" when w.Length >= 3 && TryTime(w[1], out var a) && TryTime(w[2], out var b):
                    await gestures.CreateAsync(a, b, w.Length > 3 && string.Equals(w[3], "allday", StringComparison.Ordinal), ct).ConfigureAwait(true);
                    return "ok";
                case "fail-next" when sample is not null:
                    sample.FailNext = true;
                    return "ok";
                case "writes" when sample is not null:
                    return string.Join(';', sample.Writes);
                default:
                    return "error cal-drag move ID TIME | resize ID TIME | create START END [allday] | fail-next | writes";
            }
        });

        yield return new CalendarCommand("cal-click", (w, ct) => ClickAsync(vm, w, ct));

        yield return new CalendarCommand("cal-bulk", (w, _) =>
        {
            if (sample is null || w.Length < 2 || !int.TryParse(w[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tasks) || !int.TryParse(w[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var events))
            {
                return Task.FromResult("error cal-bulk TASKS EVENTS (sample runs only)");
            }

            sample.AddBulk(tasks, events);
            return Task.FromResult("ok");
        });

        yield return new CalendarCommand("cal-perf", (w, _) =>
        {
            if (w.Length == 0 || !TryView(w[0], out var view))
            {
                return Task.FromResult("error cal-perf day|week|month|agenda");
            }

            vm.SetView(view == CalendarViewKind.Day ? CalendarViewKind.Month : CalendarViewKind.Day);
            var watch = Stopwatch.StartNew();
            vm.SetView(view);
            watch.Stop();
            return Task.FromResult(string.Create(CultureInfo.InvariantCulture, $"{view.ToString().ToLowerInvariant()} {watch.Elapsed.TotalMilliseconds:0.0} ms occurrences {vm.Occurrences.Count}"));
        });
    }

    private static async Task<string> ClickAsync(CalendarViewModel vm, string[] w, CancellationToken ct)
    {
        switch (w.FirstOrDefault())
        {
            case "event" when w.Length > 1:
                CalendarUi.Select(w[1]);
                return "ok";
            case "select" when w.Length > 1:
                CalendarUi.Select(w[1]);
                return "ok";
            case "open" when w.Length > 1:
                await vm.OpenAsync(w[1], ct).ConfigureAwait(true);
                return "ok";
            case "slot" when w.Length > 1 && TryTime(w[1], out var slot):
                CalendarUi.QuickCreate(slot, slot, allDay: false);
                return "ok";
            case "day" when w.Length > 1 && TryDate(w[1], out var day):
                vm.OpenDay(day);
                return "ok";
            case "mini" when w.Length > 1 && TryDate(w[1], out var picked):
                if (vm.View == CalendarViewKind.Tasks)
                {
                    vm.ToggleTaskDay(picked.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
                else
                {
                    vm.ShowDay(picked);
                }

                return "ok";
            case "more" when w.Length > 1 && TryDate(w[1], out var more):
                vm.OpenDay(more);
                return "ok";
            case "toggle" when w.Length > 1:
                vm.ToggleCalendar(w[1]);
                return "ok";
            case "search":
                vm.SearchText = string.Join(' ', w.Skip(1));
                return "ok";
            case "allday":
                vm.ToggleAllDay();
                return "ok";
            case "retry":
                await vm.LoadAsync(ct).ConfigureAwait(true);
                return "ok";
            case "print":
                CalendarScreen.Current?.PrintPreview.Show();
                return CalendarScreen.Current is null ? "error the calendar screen is not shown" : "ok";
            case "print-close":
                CalendarScreen.Current?.PrintPreview.Hide();
                return "ok";
            case "hover" when w.Length > 1:
            {
                var o = vm.Occurrences.FirstOrDefault(x => string.Equals(x.Id, w[1], StringComparison.Ordinal));
                if (o is null || CalendarScreen.Current is not { } screen)
                {
                    return "error no such occurrence on screen";
                }

                screen.HoverCard.Show(vm.DetailOf(o));
                return "ok";
            }

            case "menu" when w.Length > 2:
                CalendarUi.ShowMenu(w[1] switch { "slot" => CalendarMenuIds.Slot, "calendar" => CalendarMenuIds.Calendar, _ => CalendarMenuIds.Event }, 300, 200, occurrenceId: string.Equals(w[1], "event", StringComparison.Ordinal) ? w[2] : null, calendarId: string.Equals(w[1], "calendar", StringComparison.Ordinal) ? w[2] : null);
                return "ok";
            default:
                return "error cal-click event|open|slot|day|mini|more|toggle|search|allday|retry|print|hover|menu ...";
        }
    }

    private static bool TryView(string word, out CalendarViewKind view)
    {
        view = word.ToLowerInvariant() switch
        {
            "day" => CalendarViewKind.Day,
            "week" => CalendarViewKind.Week,
            "month" => CalendarViewKind.Month,
            "agenda" => CalendarViewKind.Agenda,
            "tasks" => CalendarViewKind.Tasks,
            _ => (CalendarViewKind)(-1),
        };
        return (int)view >= 0;
    }

    private static bool TryDate(string text, out DateOnly day) => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static bool TryTime(string text, out DateTime time) => DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private static string Summary(CalendarViewModel vm) =>
        string.Create(CultureInfo.InvariantCulture, $"view {CalendarViewModel.ViewName(vm.View)} anchor {vm.Anchor:yyyy-MM-dd} range {vm.Range.From:yyyy-MM-dd}..{vm.Range.To:yyyy-MM-dd} occurrences {vm.Occurrences.Count}");

    private static string Dump(CalendarViewModel vm, string what)
    {
        var root = new JsonObject
        {
            ["view"] = CalendarViewModel.ViewName(vm.View),
            ["anchor"] = vm.Anchor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["from"] = vm.Range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["to"] = vm.Range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["title"] = vm.Title,
            ["loading"] = vm.IsLoading,
            ["error"] = vm.LoadError,
            ["selected"] = vm.SelectedId,
            ["search"] = vm.SearchText,
            ["count"] = vm.Occurrences.Count,
        };
        var calendars = new JsonArray();
        foreach (var c in vm.Calendars)
        {
            calendars.Add(new JsonObject { ["id"] = c.Id, ["name"] = c.Name, ["color"] = c.Color, ["visible"] = c.Visible });
        }

        root["calendars"] = calendars;
        switch (what)
        {
            case "occurrences":
                var list = new JsonArray();
                foreach (var o in vm.Occurrences)
                {
                    list.Add(new JsonObject
                    {
                        ["id"] = o.Id,
                        ["title"] = o.Title,
                        ["start"] = o.Start.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                        ["end"] = o.End.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture),
                        ["allDay"] = o.AllDay,
                        ["calendar"] = o.Calendar,
                        ["kind"] = o.Kind.ToString(),
                    });
                }

                root["occurrences"] = list;
                break;
            case "layout":
                var columns = vm.Columns;
                var timed = new JsonArray();
                foreach (var b in WeekLayout.Timed(columns, vm.Occurrences, vm.Settings.HourFrom, vm.Settings.HourTo))
                {
                    timed.Add(new JsonObject { ["id"] = b.Occurrence.Id, ["col"] = b.Column, ["top"] = Math.Round(b.Top, 1), ["height"] = Math.Round(b.Height, 1), ["lane"] = b.Lane, ["lanes"] = b.Lanes });
                }

                var (bars, rows) = WeekLayout.AllDay(columns, vm.Occurrences);
                var strip = new JsonArray();
                foreach (var bar in bars)
                {
                    strip.Add(new JsonObject { ["id"] = bar.Occurrence.Id, ["col"] = bar.Column, ["span"] = bar.Span, ["row"] = bar.Row });
                }

                root["columns"] = columns.Count;
                root["timed"] = timed;
                root["allDay"] = new JsonObject
                {
                    ["rows"] = rows,
                    ["visibleRows"] = CalendarGridRules.AllDayVisibleRows(rows, vm.AllDayExpanded),
                    ["height"] = CalendarGridRules.AllDayHeight(CalendarGridRules.AllDayVisibleRows(rows, vm.AllDayExpanded)),
                    ["expanded"] = vm.AllDayExpanded,
                    ["expandable"] = CalendarGridRules.AllDayExpandable(rows),
                    ["bars"] = strip,
                };
                break;
            case "agenda":
                var agenda = AgendaRules.Rows(vm.Occurrences, vm.Range.From, vm.Range.To, vm.TodayDate, vm.Settings.Core.TwelveHour);
                root["agendaRows"] = agenda.Count;
                root["agendaHeads"] = new JsonArray(agenda.Where(r => r.Header.Length > 0).Take(12).Select(r => (JsonNode?)JsonValue.Create(r.Header)).ToArray());
                break;
            case "month":
                var cells = MonthLayout.Cells(vm.Range.From, vm.Anchor, vm.TodayDate, vm.Occurrences, vm.Settings);
                root["cells"] = new JsonArray(cells.Where(c => c.Items.Count > 0).Select(c => (JsonNode?)new JsonObject { ["day"] = c.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["items"] = c.Items.Count, ["more"] = c.More }).ToArray());
                break;
            default:
                break;
        }

        return root.ToJsonString();
    }
}
#endif
