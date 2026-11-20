namespace Barbican.Core.Plugins.Notifications;

/// <summary>Sanitized content for a host-attributed add-on toast.</summary>
public sealed record PluginToast(PluginToastKind Kind, string Message);
