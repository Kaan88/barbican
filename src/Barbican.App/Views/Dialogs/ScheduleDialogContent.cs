// Mirrors: Bulwark components/email/schedule-send-dialog.tsx:97-146 (title, "Pick when to send it.", the presets as rows with the time at the end, the date and time field, the error under it in the destructive colour,
// Cancel outline then the confirm button; Enter in the field schedules, Escape closes); locales/en/common.json:1175-1176; I-241; CP-07. Draws ScheduleDialogRequest inside the dialog host over the ScheduleDialogViewModel
// of send-pipeline: the composer's Schedule send and the Scheduled view's Reschedule show the same card. The content draws its own buttons, so the host's OK and Cancel stay hidden.

using Barbican.App.Shell;
using Barbican.App.Theme;
using Barbican.Presentation.Compose;
using Barbican.Presentation.Compose.Pipeline;
using Barbican.Presentation.Dialogs;
using Microsoft.UI.Xaml.Automation;

namespace Barbican.App.Views.Dialogs;

/// <summary>The schedule dialog: the composer's Schedule send and the Scheduled view's Reschedule.</summary>
public sealed class ScheduleDialogContent : IDialogContent
{
    public bool CanDraw(object request) => request is ScheduleDialogRequest;

    /// <inheritdoc />
    public bool OwnButtons => true;

    /// <inheritdoc />
    public Control? InitialFocus(UIElement content) => content is FrameworkElement { Tag: Control field } ? field : null;

    public UIElement Create(ActiveDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var model = ((ScheduleDialogRequest)dialog.Request).Model;
        _ = model.Completion.ContinueWith(_ => dialog.Complete(true), TaskScheduler.FromCurrentSynchronizationContext());
        return Card(model, () => dialog.Complete(null));
    }

    // one click for the usual times (schedule-send-dialog.tsx:116-131: rounded-md px-3 py-2 text-sm, hover bg-muted)
    private static StackPanel Presets(ScheduleDialogViewModel state, CornerRadius radius)
    {
        var presets = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < state.Presets.Count; i++)
        {
            var index = i;
            var preset = state.Presets[i];
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = preset.Label, FontSize = 14, Foreground = AppResources.Brush("Text") });
            var when = new TextBlock { Text = preset.When, FontSize = 14, Foreground = AppResources.Brush("Dim") };
            Grid.SetColumn(when, 1);
            row.Children.Add(when);
            var button = new Button { Style = (Style)Application.Current.Resources["Button.MenuItem"], Content = row, Padding = new Thickness(12, 8, 12, 8), CornerRadius = radius, FontSize = 14, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetAutomationId(button, $"composer.schedule.preset.{i}");
            AutomationProperties.SetName(button, $"{preset.Label} {preset.When}");
            button.Click += (_, _) => state.Choose(index);
            presets.Children.Add(button);
        }

        return presets;
    }

    // the error under the fields, in the destructive colour (schedule-send-dialog.tsx:144)
    private static TextBlock ErrorText(ScheduleDialogViewModel state)
    {
        var error = new TextBlock { FontSize = 14, Foreground = AppResources.Brush("Destructive"), TextWrapping = TextWrapping.Wrap, Text = state.Error, Visibility = state.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible };
        AutomationProperties.SetAutomationId(error, "composer.schedule.error");
        state.PropertyChanged += (_, args) =>
        {
            if (string.Equals(args.PropertyName, nameof(ScheduleDialogViewModel.Error), StringComparison.Ordinal))
            {
                error.Text = state.Error;
                error.Visibility = state.Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        };
        return error;
    }

    /// <summary>The body of the dialog, built over <paramref name="state"/>; <paramref name="dismiss"/> closes it without a time (Cancel, Escape).</summary>
    internal static StackPanel Card(ScheduleDialogViewModel state, Action dismiss)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dismiss);
        var radius = ViewHost.Get<StyleState>().ButtonRadius;
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "Pick when to send it.", FontSize = 14, Foreground = AppResources.Brush("Dim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -4, 0, 0) });

        panel.Children.Add(Presets(state, radius));

        var date = new TextBox { PlaceholderText = "2026-11-20", Text = state.Date, FontSize = 14, MinWidth = 140 };
        var time = new TextBox { PlaceholderText = "08:00", Text = state.Time, FontSize = 14, MinWidth = 100 };
        AutomationProperties.SetAutomationId(date, "composer.schedule.date");
        AutomationProperties.SetName(date, state.Title + " date");
        AutomationProperties.SetAutomationId(time, "composer.schedule.time");
        AutomationProperties.SetName(time, state.Title + " time");
        date.TextChanged += (_, _) => state.Date = date.Text;
        time.TextChanged += (_, _) => state.Time = time.Text;
        // a TextBox keeps its keys from the shell, so Enter (the form's submit) and Escape are read here
        void Keys(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (e.Key == global::Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                state.Date = date.Text;
                state.Time = time.Text;
                _ = state.ChooseTyped();
            }
            else if (e.Key == global::Windows.System.VirtualKey.Escape)
            {
                e.Handled = true;
                state.Cancel();
                dismiss();
            }
        }

        date.KeyDown += Keys;
        time.KeyDown += Keys;
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        fields.Children.Add(date);
        fields.Children.Add(time);
        panel.Children.Add(fields);

        panel.Children.Add(ErrorText(state));

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = DialogStyles.ButtonGap, Margin = new Thickness(0, DialogStyles.Bulwark ? 12 : 0, 0, 0) };
        footer.Children.Add(DialogStyles.FooterButton("Cancel", DialogTone.Plain, "composer.schedule.cancel", () =>
        {
            state.Cancel();
            dismiss();
        }));
        footer.Children.Add(DialogStyles.FooterButton(state.ConfirmLabel, DialogTone.Primary, "composer.schedule.ok", () => state.ChooseTyped()));
        panel.Children.Add(footer);
        panel.Tag = date;
        AutomationProperties.SetAutomationId(panel, "composer.schedule");
        return panel;
    }
}
