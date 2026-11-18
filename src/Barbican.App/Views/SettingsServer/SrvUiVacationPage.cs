// Mirrors: Bulwark components/settings/vacation-settings.tsx (the status line over the form: on or off, days, subject, message, who gets the reply, forward while away, block the calendar), Rust pim-vacation; I-435..I-437, G-022. Days are typed (no date picker: ADR 0007). Owner srv-ui.

using Barbican.Presentation.Settings;
using Barbican.Presentation.Settings.Server;
using Microsoft.UI.Xaml.Automation;
using static Barbican.App.Views.Calendar.Editors.CalendarCardKit;

namespace Barbican.App.Views.SettingsServer;

/// <summary>The Out of office settings page.</summary>
internal sealed class SrvUiVacationPage : SrvUiPage
{
    private readonly SrvUiVacationViewModel _model;

    public SrvUiVacationPage(SrvUiVacationViewModel model)
        : base(SettingsPageIds.Vacation, "Out of office", "An automatic reply for mail that arrives while you are away.")
    {
        _model = model;
        _model.PropertyChanged += (_, _) => Invalidate();
    }

    protected override Task LoadAsync() => _model.LoadAsync(CancellationToken.None);

    protected override void Render()
    {
        Reset();
        var status = SrvUiKit.Strong("Status: " + (_model.Status.Length == 0 ? "..." : _model.Status));
        AutomationProperties.SetAutomationId(status, "srv.vacation.status");
        Body.Children.Add(status);
        var form = new StackPanel { Spacing = 6 };
        form.Children.Add(Check("srv.vacation.enabled", "Send an automatic reply", _model.Enabled, v => _model.Enabled = v));
        form.Children.Add(Label("Subject"));
        form.Children.Add(Text("srv.vacation.subject", _model.Subject, v => _model.Subject = v, placeholder: "Out of office"));
        form.Children.Add(Label("Message"));
        form.Children.Add(Text("srv.vacation.body", _model.Body, v => _model.Body = v, multiline: true, placeholder: "I am away and will answer when I am back."));
        form.Children.Add(Label("Days (yyyy-MM-dd; either can stay empty)"));
        form.Children.Add(SrvUiKit.Row(Text("srv.vacation.from", _model.FromText, v => _model.FromText = v, placeholder: "First day"), Text("srv.vacation.to", _model.ToText, v => _model.ToText = v, placeholder: "Last day")));
        form.Children.Add(Label("Who gets the reply"));
        var audiences = new[] { string.Empty, "internal", "external" };
        form.Children.Add(Combo("srv.vacation.audience", ["Everyone", "Only my own domains", "Only other domains"], Math.Max(0, Array.IndexOf(audiences, _model.AudienceOnly)), i => _model.AudienceOnly = audiences[i]));
        form.Children.Add(Text("srv.vacation.domains", _model.DomainsText, v => _model.DomainsText = v, placeholder: "Own domains, comma separated"));
        form.Children.Add(Check("srv.vacation.forward", "Forward mail while I am away", _model.ForwardOn, v => _model.ForwardOn = v));
        form.Children.Add(Text("srv.vacation.forward-to", _model.ForwardTo, v => _model.ForwardTo = v, placeholder: "Forward to (address)"));
        form.Children.Add(Check("srv.vacation.forward-keep", "Keep a copy in the inbox", _model.ForwardKeep, v => _model.ForwardKeep = v));
        form.Children.Add(Check("srv.vacation.calendar", "Block my calendar for these days", _model.BlockCalendar, v => _model.BlockCalendar = v));
        Body.Children.Add(SrvUiKit.Card(form, "srv.vacation.form"));
        Body.Children.Add(SrvUiKit.Error(_model.Error));
        if (_model.Message.Length > 0)
        {
            Body.Children.Add(SrvUiKit.Dim(_model.Message));
        }

        Body.Children.Add(Btn("Save", Barbican.Presentation.Compose.DialogTone.Primary, "srv.vacation.save", () => SrvUiKit.Run(() => _model.SaveAsync(CancellationToken.None)), !_model.IsLoading));
    }
}
