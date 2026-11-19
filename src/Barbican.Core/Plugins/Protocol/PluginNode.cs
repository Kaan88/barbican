namespace Barbican.Core.Plugins.Protocol;
internal sealed record PluginNode(PluginValueKind Kind, string? Text = null, bool Flag = false, byte[]? Bytes = null, int[]? Links = null, string[]? Keys = null, PluginViewType View = default, int Offset = 0, int Length = 0);
