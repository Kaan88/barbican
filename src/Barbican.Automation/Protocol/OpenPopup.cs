namespace Barbican.Automation.Protocol;

/// <summary>An open popup as the guard sees it: the type of its content and whether XAML keeps it inside the window.</summary>
public sealed record OpenPopup(string ContentType, bool ConstrainedToRootBounds);
