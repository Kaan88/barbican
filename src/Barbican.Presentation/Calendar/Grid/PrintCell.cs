// Mirrors: Bulwark components/calendar/calendar-print.tsx + lib/calendar-print.ts (the visible view as a printable page); G-008, I-312, I-341; decision D-M4-14 (an in-app print document, not the browser).
// Pure: the model is pages of cells of lines. The preview draws it in the window; the OS print path (a button of the view) is never run by an agent or a test.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One cell of a printed page: a day with its lines.</summary>
public sealed record PrintCell(string Heading, bool Muted, bool Today, IReadOnlyList<PrintLine> Lines);
