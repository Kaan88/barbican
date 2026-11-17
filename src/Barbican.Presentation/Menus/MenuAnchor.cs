// Mirrors: M2 plan section 4.2 (MenuAnchor). Contract from stage F of M2; owner menus.

namespace Barbican.Presentation.Menus;

/// <summary>Where a menu opens: window coordinates, or an element by automation id (the row's "..." button).</summary>
public readonly record struct MenuAnchor(double X, double Y, string? ElementAutomationId);
