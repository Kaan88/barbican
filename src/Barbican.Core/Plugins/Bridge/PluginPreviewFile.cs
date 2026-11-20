// Mirrors Bulwark lib/plugin-sandbox/host-api-file-viewer.ts:26-102.
namespace Barbican.Core.Plugins.Bridge;

/// <summary>Detached file bytes for the in-window add-on preview.</summary>
public sealed record PluginPreviewFile(string Name, string ContentType, byte[] Bytes);
