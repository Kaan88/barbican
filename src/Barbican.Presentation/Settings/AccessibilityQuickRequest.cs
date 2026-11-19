using Barbican.Presentation.Dialogs;

namespace Barbican.Presentation.Settings;

/// <summary>The in-window quick accessibility panel request.</summary>
public sealed record AccessibilityQuickRequest(AccessibilityQuickModel Model) : DialogRequest<bool>("Accessibility options");
