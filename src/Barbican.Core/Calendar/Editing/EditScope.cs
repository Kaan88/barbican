// Mirrors: Rust cal_editor.rs scope choice (this / following / all); Bulwark components/calendar/recurrence-scope-dialog.tsx, lib/recurrence-scope-edit.ts. Contract from the M4 plan section 4.3; owner cal-edit.

namespace Barbican.Core.Calendar.Editing;

/// <summary>Which part of a series an edit or a delete applies to.</summary>
public enum EditScope
{
    /// <summary>Not a series: the event itself.</summary>
    WholeEvent,

    /// <summary>This occurrence only (an override under its recurrence id).</summary>
    Occurrence,

    /// <summary>This occurrence and every later one (the series is split).</summary>
    Following,

    /// <summary>The whole series.</summary>
    All,
}
