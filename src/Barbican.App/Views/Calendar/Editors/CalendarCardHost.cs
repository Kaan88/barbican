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

// Bulwark: the event and task editors are a 400 px sheet at the end of the view, full height under the toolbar, on the page ground with a hairline at its start and no scrim (calendar-app.tsx:2631,2661); the other cards are dialogs on a scrim with rounded-lg corners (8).
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
        SizeChanged += (_, _) => FitSheet(_card.Width is 400);
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

        ApplyShape(card);
        _scrim.Visibility = content is null ? Visibility.Collapsed : Visibility.Visible;
        _question.Child = _host.Question is { } q2 ? QuestionCard(q2) : null;
        _questionScrim.Visibility = _host.Question is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Bulwark draws the event and task editors as a side sheet and every other card as a dialog; Bento and Nordic keep the centred card.</summary>
    private void ApplyShape(object? card)
    {
        var look = ViewHost.Get<StyleState>();
        var bulwark = look.IsBulwark;
        var sheet = bulwark && card is EventEditorViewModel or TaskEditorViewModel;
        _scrim.Background = sheet ? null : new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(0x66, 0, 0, 0));
        _card.CornerRadius = sheet ? new CornerRadius(0) : bulwark ? look.DialogRadius : new CornerRadius(16);
        _card.Background = AppResources.Brush(bulwark ? "Bg" : "Popup");
        _card.BorderBrush = AppResources.Brush("Line");
        _card.BorderThickness = sheet ? new Thickness(1, 0, 0, 0) : bulwark ? new Thickness(1) : new Thickness(0);
        _card.HorizontalAlignment = sheet ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        _card.VerticalAlignment = sheet ? VerticalAlignment.Stretch : VerticalAlignment.Center;
        _card.Width = sheet ? 400 : double.NaN;
        _card.MaxWidth = sheet ? 400 : 560;
        _card.Padding = new Thickness(sheet ? 16 : 20);
        _scrim.Padding = sheet ? new Thickness(0, look.CalToolbarHeight + 1, 0, 0) : new Thickness(0);
        _question.CornerRadius = bulwark ? look.DialogRadius : new CornerRadius(16);
        _question.Background = AppResources.Brush(bulwark ? "Bg" : "Popup");
        _question.BorderBrush = AppResources.Brush("Line");
        _question.BorderThickness = bulwark ? new Thickness(1) : new Thickness(0);
        FitSheet(sheet);
    }

    // the body of a sheet scrolls inside the room under the toolbar; a dialog keeps its own cap
    private void FitSheet(bool sheet)
    {
        if (_card.Child is not StackPanel panel)
        {
            return;
        }

        var look = ViewHost.Get<StyleState>();
        if (sheet)
        {
            panel.MinWidth = 0;
        }

        foreach (var child in panel.Children)
        {
            if (child is ScrollViewer scroll)
            {
                scroll.MaxHeight = sheet && ActualHeight > 0 ? Math.Max(200, ActualHeight - look.CalToolbarHeight - 1 - 130) : 560;
            }
        }
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
