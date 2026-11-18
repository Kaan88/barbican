// Mirrors: Rust cal_extra.rs CxAgendaRow; Bulwark components/calendar/calendar-agenda-view.tsx. One row of the Agenda list with what the template binds to (brushes and visibilities are decided here, not in XAML).

using Barbican.Core.Calendar;
using Barbican.Presentation.Calendar.Grid;
using Windows.UI.Text;

namespace Barbican.App.Views.Calendar;

/// <summary>An <see cref="AgendaRow"/> with its colours.</summary>
public sealed class AgendaItem(AgendaRow row, string color, bool selected)
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

    public Brush TitleBrush => CalendarVisuals.Palette(Row.Empty || Row.Done ? "Faint" : "Body");

    public TextDecorations TitleDecoration => Row.Done ? TextDecorations.Strikethrough : TextDecorations.None;

    public Brush Background => selected ? CalendarVisuals.Palette("Sel") : Clear;

    public Visibility HeaderLine => string.IsNullOrEmpty(Row.Header) ? Visibility.Collapsed : Visibility.Visible;
}
