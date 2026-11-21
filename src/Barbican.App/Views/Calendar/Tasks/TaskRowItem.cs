#pragma warning disable CA1822 // x:Bind binds instance members
// Mirrors: Bulwark components/calendar/task-list-view.tsx (a row: tick, title, notes line, due, list); Rust cal_extra.rs render_tasks_view. One row of the Tasks list with what the template binds to.

using Barbican.App.Theme;
using Barbican.Presentation.Calendar.Tasks;
using Windows.UI.Text;

namespace Barbican.App.Views.Calendar.Tasks;

/// <summary>A <see cref="TaskRow"/> with its brushes.</summary>
public sealed class TaskRowItem(TaskRow row, bool selected)
{
    private static readonly SolidColorBrush Clear = new(Windows.UI.Color.FromArgb(0, 0, 0, 0));

    public TaskRow Row { get; } = row;

    public string Title => Row.Title;

    public string Notes => Row.Notes;

    public Visibility NotesVisible => Row.Notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string Due => Row.Due;

    public string ListName => Row.ListName;

    public string Tick => Row.Done ? "☑" : "☐";

    public Brush Dot => CalendarVisuals.SolidBrush(Row.Color);

    public Brush TitleBrush => AppResources.Brush(Row.Finished ? "Faint" : "Body");

    public TextDecorations TitleDecoration => Row.Done ? TextDecorations.Strikethrough : TextDecorations.None;

    // 0 dim, 1 today, 2 overdue (TaskRow.DueRank)
    public Brush DueBrush => AppResources.Brush(Row.DueRank switch { 2 => CalendarVisuals.Bulwark ? "Destructive" : "RoseFg", 1 => CalendarVisuals.Bulwark ? "Warning" : "Accent", _ => "Dim" });

    public Brush Background => selected ? AppResources.Brush(CalendarVisuals.Bulwark ? "FeedbackSelected" : "Sel") : Clear;

    // ---- Bulwark (task-list-view.tsx:317-360) ----

    public Brush Muted => AppResources.Brush("Dim");

    public Brush RuleBrush => AppResources.Brush("Line");

    /// <summary>The round check: a muted ring, the success colour once done.</summary>
    public Brush CheckBorder => Row.Done ? AppResources.Brush("Success") : AppResources.Brush("Faint");

    public Brush CheckFill => Row.Done ? AppResources.Brush("Success") : Clear;

    public string CheckMark => Row.Done ? "✓" : string.Empty;

    public Brush CheckMarkBrush => AppResources.Brush("SuccessFg");

    public Visibility DueVisible => Row.Due.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
