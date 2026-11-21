#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Rust cal_extra.rs CxAgendaRow; Bulwark components/calendar/calendar-agenda-view.tsx:233-246 (a day: a 36 px date circle, the weekday and month, Today or Tomorrow; border-b between days), :281-330 (an event row: the time, a 9 px calendar dot, the title text-sm medium, the place text-xs muted). One row of the Agenda list with what the template binds to (brushes and visibilities are decided here, not in XAML).

using System.Globalization;
using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;
using Windows.UI.Text;

namespace Barbican.App.Views.Calendar;

/// <summary>An <see cref="AgendaRow"/> with its colours.</summary>
public sealed class AgendaItem(AgendaRow row, string color, bool selected, bool lastOfDay = false, bool tomorrow = false)
{
    private static readonly SolidColorBrush Clear = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public AgendaRow Row { get; } = row;

    public string Header => Row.Header;

    public string Time => Row.Time;

    public string Title => Row.Empty ? "Nothing scheduled" : Row.Title;

    public string Location => Row.Location;

    public bool IsTask => Row.Kind == CalendarOccurrenceKind.Task;

    public Brush Dot => CalendarVisuals.SolidBrush(color);

    public Visibility DotVisible => Row.Empty || IsTask ? Visibility.Collapsed : Visibility.Visible;

    public Visibility TaskMarkVisible => IsTask ? Visibility.Visible : Visibility.Collapsed;

    public string TaskMark => Row.Done ? "✓" : "○";

    public Brush HeaderBrush => CalendarVisuals.Palette(Row.Today ? "Accent" : "Text");

    public Brush TitleBrush => CalendarVisuals.Palette(Row.Empty || Row.Done ? "Faint" : CalendarVisuals.Bulwark ? "Text" : "Body");

    public TextDecorations TitleDecoration => Row.Done ? TextDecorations.Strikethrough : TextDecorations.None;

    /// <summary>The selected row: the selection fill (the accent in Bulwark); the rows snap and the template draws the hover.</summary>
    public Brush Background => selected ? CalendarVisuals.Palette(CalendarVisuals.Bulwark ? "FeedbackSelected" : "Sel") : Clear;

    public Visibility HeaderLine => string.IsNullOrEmpty(Row.Header) ? Visibility.Collapsed : Visibility.Visible;

    // ---- Bulwark ----

    /// <summary>The date column shows on the first row of a day only.</summary>
    public Visibility DateVisible => HeaderLine;

    public string DayNumber => Row.Day.Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>"Sat, Nov" (intl weekday short, month short).</summary>
    public string DayName => Row.Day.ToString("ddd, MMM", CultureInfo.InvariantCulture);

    /// <summary>"Today" or "Tomorrow" under the day name, else nothing.</summary>
    public string Relative => Row.Today ? "Today" : tomorrow ? "Tomorrow" : string.Empty;

    public Visibility RelativeVisible => Relative.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Brush DayCircle => Row.Today ? CalendarVisuals.Palette("Accent") : Clear;

    public Brush DayCircleText => CalendarVisuals.Palette(Row.Today ? "OnAccent" : "Text");

    public FontWeight DayCircleWeight => Row.Today ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;

    public Brush DayNameBrush => CalendarVisuals.Palette(Row.Today ? "Accent" : "Dim");

    /// <summary>border-b under the last row of a day.</summary>
    public Thickness DayRule => lastOfDay ? new Thickness(0, 0, 0, 1) : new Thickness(0);

    public Brush RuleBrush => CalendarVisuals.Palette("Line");

    public Brush Muted => CalendarVisuals.Palette("Dim");

    public Visibility LocationVisible => Row.Location.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The first row of a day carries the date circle, so it is taller.</summary>
    public double RowMinHeight => HeaderLine == Visibility.Visible ? 52 : 36;
}
