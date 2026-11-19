// Mirrors: Bulwark components/calendar/event-modal.tsx, quick-create popover, recurrence-scope-dialog.tsx; ADR 0007 (the card is in the window, never a popup). I-300, I-324, I-332..I-340.
// Owner cal-ui. Draws CalendarEditorHost.Card on a scrim and, above it, CalendarEditorHost.Question. cal-views places this view over the calendar screen (one line in its shell host).

#pragma warning disable CA1859

using System.ComponentModel;
using Barbican.App.Theme;
using Barbican.App.Shell;
using Barbican.Presentation.Dialogs;
using Barbican.Presentation.Settings;
using Barbican.Presentation.Calendar.Editors;
using Barbican.Presentation.Calendar.Editors.Dialogs;
using Barbican.Presentation.Calendar.Tasks;
using Barbican.Presentation.Compose;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Input;

namespace Barbican.App.Views.Calendar.Editors;

/// <summary>The card layer of the calendar screen.</summary>
public sealed partial class CalendarCardHost : Grid
{
    private readonly CalendarEditorHost _host;
    private readonly Border _scrim = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly Border _card = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), MaxWidth = 560, Background = AppResources.Brush("Popup") };
    private readonly Border _questionScrim = new() { Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0, 0, 0)), Visibility = Visibility.Collapsed };
    private readonly Border _question = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(20), CornerRadius = new CornerRadius(16), MaxWidth = 460, Background = AppResources.Brush("Popup") };
    private INotifyPropertyChanged? _watched;

    public CalendarCardHost(CalendarEditorHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        AutomationProperties.SetAutomationId(this, "cal.cards");
        _scrim.Child = _card;
        _scrim.Tapped += OnScrimTapped;
        _card.Tapped += (_, e) => e.Handled = true;
        _questionScrim.Child = _question;
        _question.Tapped += (_, e) => e.Handled = true;
        Children.Add(_scrim);
        Children.Add(_questionScrim);
        host.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        Refresh();
    }

    private async Task PickZoneAsync(string selected, Action<string> apply)
    {
        var answer = await ViewHost.Get<IDialogService>().ShowAsync(new TimeZonePickerRequest(selected, RequireSupportedZone: true), CancellationToken.None).ConfigureAwait(true);
        if (answer is not null)
        {
            apply(answer);
            ForceRedraw();
        }
    }

    private void OnScrimTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_host.Question is null)
        {
            _host.Escape();
        }
    }

    /// <summary>Redraws the card and the question from the host.</summary>
    public void Refresh()
    {
        var card = _host.Card;
        UIElement? content = card switch
        {
            EventEditorViewModel e => EventCard(e),
            TaskEditorViewModel t => TaskCard(t),
            QuickCreateViewModel q => QuickCard(q),
            CreateCalendarViewModel c => CreateCalendarCard(c),
            SubscriptionViewModel s => SubscriptionCard(s),
            ImportViewModel i => ImportCard(i),
            _ => null,
        };
        if (!ReferenceEquals(_watched, card))
        {
            _watched = card as INotifyPropertyChanged;
        }

        if (!ReferenceEquals(_card.Tag, card))
        {
            _card.Tag = card;
            _card.Child = content;
        }

        _scrim.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
        _question.Child = _host.Question is { } q2 ? QuestionCard(q2) : null;
        _questionScrim.Visibility = _host.Question is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement QuestionCard(ComposerDialog dialog)
    {
        var panel = CalendarCardKit.Card("cal.question." + dialog.Id, dialog.Title, 320);
        panel.Children.Add(CalendarCardKit.Body(dialog.Message));
        foreach (var line in dialog.Lines)
        {
            panel.Children.Add(CalendarCardKit.Body("•  " + line));
        }

        if (string.Equals(dialog.Id, "scope", StringComparison.Ordinal) && dialog.Lines.Count > 0)
        {
            panel.Children.Add(CalendarCardKit.Check("cal.question.notify", "Notify guests", _host.Notify, v => _host.Notify = v));
        }

        var buttons = new List<UIElement>();
        foreach (var choice in dialog.Choices)
        {
            var id = choice.Id;
            buttons.Add(CalendarCardKit.Btn(choice.Label, choice.Tone, "cal.question." + id, () => dialog.Complete(id)));
        }

        panel.Children.Add(CalendarCardKit.Buttons([.. buttons]));
        return panel;
    }
}
