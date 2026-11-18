// Mirrors: Bulwark components/calendar/event-modal.tsx (fields, recurrence, show as, visibility, guests, alerts, edit footer); I-332..I-339.

using System.Globalization;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Compose;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

#pragma warning disable MA0051 // the card builders are linear lists of fields, one per control

namespace Barbican.App.Views.Calendar.Editors;

public sealed partial class CalendarCardHost
{
    private StackPanel EventCard(EventEditorViewModel e)
    {
        var panel = Card("cal.event", e.Heading, 420);
        var body = new StackPanel { Spacing = 4 };
        var read = e.ReadOnly;
        body.Children.Add(Text("cal.event.title", e.Title, v => e.Title = v, placeholder: "Add title"));
        body.Children.Add(Label("Calendar"));
        var calendars = e.Calendars.ToList();
        body.Children.Add(Combo("cal.event.calendar", calendars.Select(c => c.Name), Math.Max(0, calendars.FindIndex(c => string.Equals(c.Id, e.CalendarId, StringComparison.Ordinal))), i => e.CalendarId = calendars[i].Id));
        body.Children.Add(Check("cal.event.allday", "All day", e.AllDay, v => { e.AllDay = v; ForceRedraw(); }));
        body.Children.Add(Label("Starts (yyyy-MM-dd, HH:mm)"));
        body.Children.Add(DateTimeRow("cal.event.start", e.StartDate, e.StartTime, d => e.StartDate = d, t => e.StartTime = t, e.AllDay));
        body.Children.Add(Label("Ends"));
        body.Children.Add(DateTimeRow("cal.event.end", e.EndDate, e.EndTime, d => e.EndDate = d, t => e.EndTime = t, e.AllDay));
        body.Children.Add(Body("Time zone: " + e.ZoneLabel));
        body.Children.Add(Label("Repeat"));
        body.Children.Add(Combo("cal.event.repeat", EventEditorViewModel.RepeatChoices, e.Repeat, i => { e.Repeat = i; ForceRedraw(); }));
        if (e.CustomOpen)
        {
            body.Children.Add(Body(e.Recurrence.Summary));
        }

        body.Children.Add(Label("Location"));
        body.Children.Add(Text("cal.event.location", e.Location, v => e.Location = v));
        body.Children.Add(Label("Description"));
        body.Children.Add(Text("cal.event.description", e.Description, v => e.Description = v, multiline: true));
        body.Children.Add(Label("Show as"));
        body.Children.Add(Combo("cal.event.showas", EventEditorViewModel.ShowAsChoices, e.FreeBusy, i => e.FreeBusy = i));
        body.Children.Add(Label("Visibility"));
        body.Children.Add(Combo("cal.event.visibility", EventEditorViewModel.VisibilityChoices, e.Privacy, i => e.Privacy = i));
        body.Children.Add(Label("Guests"));
        foreach (var g in e.Guests)
        {
            var guest = g;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(Body(guest.Display + (guest.Answer.Length > 0 ? "  (" + guest.Answer + ")" : string.Empty)));
            row.Children.Add(Btn("Remove", DialogTone.Plain, "cal.event.guest.remove", () => { e.RemoveGuest(guest); ForceRedraw(); }));
            body.Children.Add(row);
        }

        var input = Text("cal.event.guest.input", e.GuestInput, v => e.GuestInput = v, placeholder: "Add guests (comma separated)");
        input.KeyDown += (_, k) =>
        {
            if (k.Key == Windows.System.VirtualKey.Enter && e.CommitGuest())
            {
                k.Handled = true;
                ForceRedraw();
            }
        };
        body.Children.Add(input);
        if (e.HasGuests && e.AvailabilitySupported)
        {
            body.Children.Add(Btn("Find a time", DialogTone.Plain, "cal.event.findtime", () => Run(e.FindATimeAsync(CancellationToken.None))));
        }

        body.Children.Add(Label("Alerts"));
        body.Children.Add(Body(e.UseDefaultAlerts ? "Default: " + e.DefaultAlertsLabel : "Custom"));
        foreach (var a in e.Alerts)
        {
            var alert = a;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            if (alert.HasAmount)
            {
                row.Children.Add(Text("cal.event.alert.value", alert.Value.ToString(CultureInfo.InvariantCulture), v =>
                {
                    if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                    {
                        alert.Value = n;
                    }
                }));
            }

            row.Children.Add(Combo("cal.event.alert.unit", AlertRowViewModel.Units, alert.Unit, i => { alert.Unit = i; ForceRedraw(); }));
            row.Children.Add(Combo("cal.event.alert.action", AlertRowViewModel.Actions, alert.Action, i => alert.Action = i));
            row.Children.Add(Btn("Remove", DialogTone.Plain, "cal.event.alert.remove", () => { e.RemoveAlert(alert); ForceRedraw(); }));
            body.Children.Add(row);
        }

        body.Children.Add(Btn(e.UseDefaultAlerts ? "Customize alerts" : "Add alert", DialogTone.Plain, "cal.event.alert.add", () =>
        {
            if (e.UseDefaultAlerts)
            {
                e.CustomizeAlerts();
            }
            else
            {
                e.AddAlert();
            }

            ForceRedraw();
        }));
        if (e.Guests.Count > 0)
        {
            body.Children.Add(Check("cal.event.notify", "Notify guests", e.Notify, v => e.Notify = v));
        }

        var error = ErrorLine(e.Error);
        e.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(EventEditorViewModel.Error), StringComparison.Ordinal))
            {
                DispatcherQueue.TryEnqueue(() => SetError(error, e.Error));
            }
        };
        body.Children.Add(error);
        panel.Children.Add(new ScrollViewer { Content = body, MaxHeight = 560 });
        var buttons = new List<UIElement>();
        if (!e.IsNew && !read)
        {
            buttons.Add(Btn("Delete", DialogTone.Danger, "cal.event.delete", () => Run(e.DeleteAsync(CancellationToken.None))));
        }

        buttons.Add(Btn(read ? "Close" : "Cancel", DialogTone.Plain, "cal.event.cancel", () => Run(e.RequestCloseAsync(CancellationToken.None))));
        if (!read)
        {
            buttons.Add(Btn("Save", DialogTone.Primary, "cal.event.save", () => Run(e.SaveAsync(CancellationToken.None))));
        }

        panel.Children.Add(Buttons([.. buttons]));
        return panel;
    }

    private static StackPanel DateTimeRow(string id, DateOnly date, string time, Action<DateOnly> setDate, Action<string> setTime, bool allDay)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(Text(id + ".date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), v =>
        {
            if (DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                setDate(d);
            }
        }));
        if (!allDay)
        {
            row.Children.Add(Text(id + ".time", time, setTime));
        }

        return row;
    }

    /// <summary>Rebuilds the open card (a list inside it changed).</summary>
    private void ForceRedraw()
    {
        _card.Tag = null;
        Refresh();
    }
}
