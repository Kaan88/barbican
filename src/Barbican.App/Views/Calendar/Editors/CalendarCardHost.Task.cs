// Mirrors: Bulwark components/calendar/task-modal.tsx; I-343 (task editor), I-324 (quick create popover).

using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Compose;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.Calendar.Editors;

public sealed partial class CalendarCardHost
{
    private StackPanel TaskCard(TaskEditorViewModel t)
    {
        var panel = Card("cal.task", t.Heading);
        panel.Children.Add(Text("cal.task.title", t.Title, v => t.Title = v, placeholder: "Task title"));
        panel.Children.Add(Label("Notes"));
        panel.Children.Add(Text("cal.task.notes", t.Notes, v => t.Notes = v, multiline: true));
        panel.Children.Add(Label("List"));
        var lists = t.Lists.ToList();
        panel.Children.Add(Combo("cal.task.list", lists.Select(l => l.Name), Math.Max(0, lists.FindIndex(l => string.Equals(l.Id, t.ListId, StringComparison.Ordinal))), i => t.ListId = lists[i].Id));
        panel.Children.Add(Label("Due (yyyy-MM-dd)"));
        panel.Children.Add(Text("cal.task.due", t.DueDate, v => t.DueDate = v));
        panel.Children.Add(Check("cal.task.hastime", "Due time", t.HasTime, v => { t.HasTime = v; ForceRedraw(); }));
        if (t.HasTime)
        {
            panel.Children.Add(Text("cal.task.duetime", t.DueTime, v => t.DueTime = v));
        }

        panel.Children.Add(Label("Priority"));
        panel.Children.Add(Combo("cal.task.priority", TaskEditorViewModel.PriorityChoices, t.Priority, i => t.Priority = i));
        panel.Children.Add(Label("Status"));
        panel.Children.Add(Combo("cal.task.progress", TaskEditorViewModel.ProgressChoices, t.Progress, i => t.Progress = i));
        panel.Children.Add(Label("Repeat"));
        panel.Children.Add(Combo("cal.task.repeat", TaskEditorViewModel.RepeatChoices, t.Repeat, i => t.Repeat = i));
        panel.Children.Add(Label("Alert"));
        panel.Children.Add(Combo("cal.task.alert", TaskEditorViewModel.AlertChoices, t.Alert, i => t.Alert = i));
        var error = ErrorLine(t.Error);
        t.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(TaskEditorViewModel.Error), StringComparison.Ordinal))
            {
                DispatcherQueue.TryEnqueue(() => SetError(error, t.Error));
            }
        };
        panel.Children.Add(error);
        var buttons = new List<UIElement>();
        if (!t.IsNew)
        {
            buttons.Add(Btn("Delete", DialogTone.Danger, "cal.task.delete", () => Run(t.DeleteAsync(CancellationToken.None))));
        }

        buttons.Add(Btn("Cancel", DialogTone.Plain, "cal.task.cancel", t.Close));
        buttons.Add(Btn("Save", DialogTone.Primary, "cal.task.save", () => Run(t.SaveAsync(CancellationToken.None))));
        panel.Children.Add(Buttons([.. buttons]));
        return panel;
    }

    private StackPanel QuickCard(QuickCreateViewModel q)
    {
        var panel = Card("cal.quick", "New event", 320);
        var (day, time) = q.Label;
        panel.Children.Add(Body(time.Length > 0 ? day + "  " + time : day));
        panel.Children.Add(Text("cal.quick.title", q.Title, v => q.Title = v, placeholder: "Add title"));
        var calendars = q.Calendars.ToList();
        panel.Children.Add(Combo("cal.quick.calendar", calendars.Select(c => c.Name), Math.Max(0, calendars.FindIndex(c => string.Equals(c.Id, q.CalendarId, StringComparison.Ordinal))), i => q.CalendarId = calendars[i].Id));
        var error = ErrorLine(q.Error);
        q.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(QuickCreateViewModel.Error), StringComparison.Ordinal))
            {
                DispatcherQueue.TryEnqueue(() => SetError(error, q.Error));
            }
        };
        panel.Children.Add(error);
        panel.Children.Add(Buttons(
            Btn("More options", DialogTone.Plain, "cal.quick.more", q.More),
            Btn("Cancel", DialogTone.Plain, "cal.quick.cancel", q.Close),
            Btn("Save", DialogTone.Primary, "cal.quick.save", () => Run(q.SaveAsync(CancellationToken.None)), q.CanSave)));
        return panel;
    }
}
