#if AUTOMATION
// Mirrors: spec/automation-protocol.md (calendar editor commands, M4 cal-ui): cal-editor-open, cal-editor-set, cal-editor-save, cal-task-add. No input is produced; the commands drive the view models the cards draw (ADR 0007),
// on the UI thread, and answer one line.

using System.Globalization;
using Barbican.App.Views.Menus.Automation;
using Barbican.Automation;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Barbican.App.Views.Calendar.Editors.Automation;

/// <summary>Registers the calendar editor automation commands.</summary>
public static class CalendarUiAutomation
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd"];

    public static IServiceCollection AddBarbicanCalendarUiAutomation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Add(services, "cal-editor-open", OpenAsync);
        Add(services, "cal-editor-set", (sp, args) =>
        {
            var answer = Set(sp, args);
            sp.GetService<CalendarCardHost>()?.Redraw();
            return Task.FromResult(answer);
        });
        Add(services, "cal-editor-save", SaveAsync);
        Add(services, "cal-task-add", AddTaskAsync);
        Add(services, "cal-reminder", (sp, args) => Task.FromResult(Remind(sp, args)));
        return services;
    }

    private static void Add(IServiceCollection services, string name, Func<IServiceProvider, string, Task<string>> run) =>
        services.AddSingleton<IAutomationCommand>(sp => new LambdaAutomationCommand(name, args => run(sp, args)));

    // cal-reminder [missed] TITLE: puts a reminder on screen (or in the missed-reminders dialog) as if the scheduler had raised it; the answer is the counts shown and missed
    private static string Remind(IServiceProvider sp, string args)
    {
        if (sp.GetRequiredService<Barbican.Presentation.Calendar.Reminders.IReminderPopup>() is not Barbican.Presentation.Calendar.Reminders.ReminderPopup popup)
        {
            return "error no reminder popup";
        }

        var text = args.Trim();
        var missed = text.StartsWith("missed", StringComparison.OrdinalIgnoreCase);
        var title = (missed ? text[6..] : text).Trim();
        var now = (sp.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow().ToUnixTimeMilliseconds();
        var start = now + (missed ? -3_600_000 : 900_000);
        var key = "auto:" + start.ToString(CultureInfo.InvariantCulture) + ":" + title;
        popup.Offer([new Barbican.Core.Calendar.Reminders.DueReminder(key, "auto-" + (title.Length == 0 ? "reminder" : title), "a1", now, start, title.Length == 0 ? "Team stand-up" : title, "Work", false)], missed);
        return "ok shown=" + popup.Shown.Count.ToString(CultureInfo.InvariantCulture) + " missed=" + popup.Missed.Count.ToString(CultureInfo.InvariantCulture);
    }

    // cal-editor-open new [yyyy-MM-dd [HH:mm]] | quick [yyyy-MM-dd [HH:mm]] | task | ID
    private static async Task<string> OpenAsync(IServiceProvider sp, string args)
    {
        var editors = sp.GetRequiredService<ICalendarEditors>();
        var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var verb = parts.Length > 0 ? parts[0] : "new";
        var now = (sp.GetService<TimeProvider>() ?? TimeProvider.System).GetLocalNow().DateTime;
        var start = now.Date.AddHours(now.Hour + 1);
        if (parts.Length > 1 && DateTime.TryParseExact(string.Join(' ', parts.Skip(1)), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
        {
            start = at;
        }

        var request = new NewEventRequest(start, start.AddHours(1), false);
        switch (verb)
        {
            case "new":
                await editors.OpenNewEventAsync(request, CancellationToken.None);
                break;
            case "quick":
                await editors.OpenQuickCreateAsync(request, CancellationToken.None);
                break;
            case "task":
                await editors.OpenNewTaskAsync(null, null, CancellationToken.None);
                break;
            default:
                await editors.OpenAsync(args.Trim(), CancellationToken.None);
                break;
        }

        return sp.GetRequiredService<CalendarEditorHost>().Card?.GetType().Name ?? "error no card";
    }

    // cal-editor-set FIELD VALUE (event: title location description calendar allday start end repeat showas visibility notify; task: title notes due priority progress list; quick: title calendar)
    private static string Set(IServiceProvider sp, string args)
    {
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "error usage: cal-editor-set FIELD VALUE";
        }

        var value = parts.Length > 1 ? parts[1] : string.Empty;
        var number = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
        var flag = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "1", StringComparison.Ordinal);
        return sp.GetRequiredService<CalendarEditorHost>().Card switch
        {
            EventEditorViewModel e => SetEvent(e, parts[0], value, number, flag),
            TaskEditorViewModel t => SetTask(t, parts[0], value, number),
            QuickCreateViewModel q => SetQuick(q, parts[0], value),
            _ => "error no editor open",
        };
    }

    private static string SetQuick(QuickCreateViewModel q, string field, string value)
    {
        switch (field)
        {
            case "title":
                q.Title = value;
                return "ok";
            case "calendar":
                q.CalendarId = value;
                return "ok";
            default:
                return "error unknown field";
        }
    }

    private static string SetEvent(EventEditorViewModel e, string field, string value, int number, bool flag)
    {
        switch (field)
        {
            case "title":
                e.Title = value;
                break;
            case "location":
                e.Location = value;
                break;
            case "description":
                e.Description = value;
                break;
            case "calendar":
                e.CalendarId = value;
                break;
            case "allday":
                e.AllDay = flag;
                break;
            case "start":
            case "end":
                if (!DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
                {
                    return "error expected yyyy-MM-dd HH:mm";
                }

                if (string.Equals(field, "start", StringComparison.Ordinal))
                {
                    e.StartDate = DateOnly.FromDateTime(at);
                    e.StartTime = at.ToString("HH:mm", CultureInfo.InvariantCulture);
                }
                else
                {
                    e.EndDate = DateOnly.FromDateTime(at);
                    e.EndTime = at.ToString("HH:mm", CultureInfo.InvariantCulture);
                }

                break;
            case "repeat":
                e.Repeat = number;
                break;
            case "showas":
                e.FreeBusy = number;
                break;
            case "visibility":
                e.Privacy = number;
                break;
            case "notify":
                e.Notify = flag;
                break;
            default:
                return "error unknown field";
        }

        return "ok";
    }

    private static string SetTask(TaskEditorViewModel t, string field, string value, int number)
    {
        switch (field)
        {
            case "title":
                t.Title = value;
                break;
            case "notes":
                t.Notes = value;
                break;
            case "due":
                t.DueDate = value;
                break;
            case "priority":
                t.Priority = number;
                break;
            case "progress":
                t.Progress = number;
                break;
            case "list":
                t.ListId = value;
                break;
            default:
                return "error unknown field";
        }

        return "ok";
    }

    private static async Task<string> SaveAsync(IServiceProvider sp, string args)
    {
        _ = args;
        switch (sp.GetRequiredService<CalendarEditorHost>().Card)
        {
            case EventEditorViewModel e:
                return await e.SaveAsync(CancellationToken.None) ? "ok" : "error " + e.Error;
            case TaskEditorViewModel t:
                return await t.SaveAsync(CancellationToken.None) ? "ok" : "error " + t.Error;
            case QuickCreateViewModel q:
                return await q.SaveAsync(CancellationToken.None) ? "ok" : "error " + q.Error;
            default:
                return "error no editor open";
        }
    }

    // cal-task-add TEXT (the Tasks view quick add, natural-language date included)
    private static async Task<string> AddTaskAsync(IServiceProvider sp, string args)
    {
        var tasks = sp.GetRequiredService<TasksViewModel>();
        tasks.Quick = args.Trim();
        return await tasks.QuickAddAsync(CancellationToken.None) ? "ok" : "error not added";
    }
}
#endif
