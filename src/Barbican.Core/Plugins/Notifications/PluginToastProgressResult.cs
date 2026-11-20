using Barbican.Core.Plugins.Bridge;
namespace Barbican.Core.Plugins.Notifications;

/// <summary>The host-issued opaque progress handle, or a safe host refusal.</summary>
public sealed record PluginToastProgressResult(string? Handle, PluginErrorCode? Error);
