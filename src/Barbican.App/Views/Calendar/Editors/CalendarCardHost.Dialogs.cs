// Mirrors: Bulwark components/calendar/{create-calendar-dialog,subscribe-dialog,import-events-dialog}.tsx; I-307, I-309, I-340.
// Dialog shell and footer: ui/dialog.tsx:131-134,181 through Views/Dialogs/DialogStyles; footers Cancel then the action: create-calendar-modal.tsx:59-75, ical-subscription-modal.tsx:101-120, ical-import-modal.tsx:288-310.

using System.Globalization;
using Barbican.Presentation.Calendar.Editors.Dialogs;
using Barbican.App.Views.Dialogs;
using Barbican.Presentation.Compose;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.Calendar.Editors;

public sealed partial class CalendarCardHost
{
    private StackPanel CreateCalendarCard(CreateCalendarViewModel c)
    {
        var panel = Card("cal.create", "Create calendar", 340);
        panel.Children.Add(Text("cal.create.name", c.Name, v => c.Name = v, placeholder: "Calendar name"));
        panel.Children.Add(Label("Colour (#rrggbb)"));
        panel.Children.Add(Text("cal.create.color", c.Color, v => c.Color = v));
        var error = ErrorLine(c.Error);
        c.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(() => SetError(error, c.Error));
        panel.Children.Add(error);
        panel.Children.Add(DialogStyles.Footer(
            DialogStyles.FooterButton("Cancel", DialogTone.Plain, "cal.create.cancel", c.Close),
            DialogStyles.FooterButton("Create", DialogTone.Primary, "cal.create.ok", () => Run(c.CreateAsync(CancellationToken.None)))));
        return panel;
    }

    private StackPanel SubscriptionCard(SubscriptionViewModel s)
    {
        var panel = Card("cal.subscription", s.Heading, 380);
        panel.Children.Add(Label("Calendar address (https or webcal)"));
        panel.Children.Add(Text("cal.subscription.url", s.Url, v => s.Url = v, placeholder: "https://"));
        panel.Children.Add(Label("Name"));
        panel.Children.Add(Text("cal.subscription.name", s.Name, v => s.Name = v));
        panel.Children.Add(Label("Colour (#rrggbb)"));
        panel.Children.Add(Text("cal.subscription.color", s.Color, v => s.Color = v));
        panel.Children.Add(Label("Refresh every"));
        var intervals = SubscriptionViewModel.Intervals.ToList();
        panel.Children.Add(Combo("cal.subscription.interval", intervals.Select(SubscriptionViewModel.IntervalLabel), s.Interval, i => s.Interval = i));
        var warning = ErrorLine(s.Warning);
        var error = ErrorLine(s.Error);
        var status = Body(s.Status);
        s.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            SetError(error, s.Error);
            SetError(warning, s.Warning);
            status.Text = s.Status;
        });
        panel.Children.Add(warning);
        panel.Children.Add(error);
        panel.Children.Add(status);
        // the destructive way out at the start (me-auto), then Refresh now, Cancel and the action (ical-subscription-modal.tsx:101-120)
        var buttons = new List<UIElement>();
        UIElement? start = null;
        if (s.IsEdit)
        {
            start = DialogStyles.FooterButton("Unsubscribe", DialogTone.Danger, "cal.subscription.unsubscribe", () => Run(s.UnsubscribeAsync(CancellationToken.None)));
            buttons.Add(DialogStyles.FooterButton("Refresh now", DialogTone.Plain, "cal.subscription.refresh", () => Run(s.RefreshAsync(CancellationToken.None))));
        }

        buttons.Add(DialogStyles.FooterButton("Cancel", DialogTone.Plain, "cal.subscription.cancel", s.Close));
        buttons.Add(DialogStyles.FooterButton(s.IsEdit ? "Save" : "Subscribe", DialogTone.Primary, "cal.subscription.save", () => Run(s.SaveAsync(CancellationToken.None))));
        panel.Children.Add(DialogStyles.Footer(start, [.. buttons]));
        return panel;
    }

    private StackPanel ImportCard(ImportViewModel m)
    {
        var panel = Card("cal.import", "Import events", 420);
        panel.Children.Add(DialogStyles.FooterButton("Choose .ics file", DialogTone.Plain, "cal.import.pick", () => Run(m.PickAsync(CancellationToken.None))));
        var info = Body(m.FileName);
        panel.Children.Add(info);
        var calendars = m.Calendars.ToList();
        panel.Children.Add(Label("Into calendar"));
        panel.Children.Add(Combo("cal.import.calendar", calendars.Select(c => c.Name), Math.Max(0, calendars.FindIndex(c => string.Equals(c.Id, m.CalendarId, StringComparison.Ordinal))), i => m.CalendarId = calendars[i].Id));
        var list = new StackPanel { Spacing = 2 };
        foreach (var item in m.Items)
        {
            var it = item;
            list.Children.Add(Check("cal.import.item", it.Title, it.Selected, v => it.Selected = v));
        }

        panel.Children.Add(new ScrollViewer { Content = list, MaxHeight = 220 });
        var alerts = Check("cal.import.alerts", "Keep the alerts from the file", m.KeepAlerts, v => m.KeepAlerts = v);
        alerts.Visibility = m.ShowAlertsPrompt ? Visibility.Visible : Visibility.Collapsed;
        panel.Children.Add(alerts);
        var error = ErrorLine(m.Error);
        var status = Body(m.Status);
        m.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            SetError(error, m.Error);
            status.Text = m.Status;
            info.Text = m.FileName;
            alerts.Visibility = m.ShowAlertsPrompt ? Visibility.Visible : Visibility.Collapsed;
        });
        panel.Children.Add(error);
        panel.Children.Add(status);
        panel.Children.Add(DialogStyles.Footer(
            DialogStyles.FooterButton("Cancel", DialogTone.Plain, "cal.import.cancel", m.Close),
            DialogStyles.FooterButton("Import " + m.SelectedCount.ToString(CultureInfo.InvariantCulture), DialogTone.Primary, "cal.import.ok", () => Run(m.ImportAsync(CancellationToken.None)))));
        return panel;
    }
}
