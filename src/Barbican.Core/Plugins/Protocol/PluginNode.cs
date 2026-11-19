// Mirrors: Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666; the tagged v1 graph is a native transport adaptation.
namespace Barbican.Core.Plugins.Protocol;
internal sealed record PluginNode(PluginValueKind Kind, string? Text = null, bool Flag = false, byte[]? Bytes = null, int[]? Links = null, string[]? Keys = null, PluginViewType View = default, int Offset = 0, int Length = 0);
