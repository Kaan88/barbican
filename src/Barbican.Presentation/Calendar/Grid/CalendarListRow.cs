// Mirrors: Rust calendar.rs (sidebar calendar rows: colour, visibility, default, shared owner, subscription); Bulwark components/calendar/calendar-sidebar-panel.tsx; B-CAL-002, 057, 058. Contract from the M4 plan section 4.5; owner cal-views.

namespace Barbican.Presentation.Calendar.Grid;

/// <summary>One calendar in the sidebar. <c>Name</c> has no account suffix (B-CAL-058); <c>Color</c> is "#rrggbb" (the 8-colour palette in order, or the viewer's colour for a shared calendar, B-CAL-002).</summary>
public sealed record CalendarListRow(string Id, string AccountKey, string Name, string Color, bool Visible, bool IsDefault, bool IsShared, bool IsSubscription, bool CanEdit);
