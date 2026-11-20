using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Bridge;

/// <summary>Validated print content. A null title requests the host-owned add-on name.</summary>
public sealed record PluginPrintRequest(string? Title, string? Html, PluginValue? Pdf);
