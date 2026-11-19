// Mirrors: Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666; the tagged v1 graph is a native transport adaptation.
namespace Barbican.Core.Plugins.Protocol;
public sealed class PluginValueFormatException : FormatException
{
    public PluginValueFormatException(string message) : base(message) { }
    public PluginValueFormatException(string message, Exception innerException) : base(message, innerException) { }
}
