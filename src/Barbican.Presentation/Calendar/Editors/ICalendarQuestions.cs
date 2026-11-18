// Mirrors: M3 composer cards (ComposerDialog: a question inside the pane, answered by its buttons, Escape cancels) and ADR 0007 (in-window, never a windowed dialog); Bulwark components/calendar/recurrence-scope-dialog.tsx.
// The calendar's card layer: one card (event editor, task editor, quick create, a dialog) on the scrim and, above it, at most one question. The views draw whatever <see cref="Card"/> and <see cref="Question"/> are.

using Barbican.Core.Calendar.Editing;
using Barbican.Presentation.Compose;
namespace Barbican.Presentation.Calendar.Editors;

/// <summary>The questions the editors ask in the pane.</summary>
public interface ICalendarQuestions
{
    /// <summary>The scope question (I-300). Null when cancelled.</summary>
    Task<(EditScope Scope, bool Notify)?> AskScopeAsync(string title, bool forDelete, bool hasGuests, CancellationToken ct);

    /// <summary>A question with choices; the chosen id, or null for Escape.</summary>
    Task<string?> AskAsync(ComposerDialog dialog, CancellationToken ct);
}
