// Mirrors: Bulwark protocol.ts ApiRequestMsg. There are deliberately no claimed authority, profile or frame fields.
using Barbican.Core.Plugins.Protocol;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginApiRequest
{
    public PluginApiRequest(string id, string method, PluginValue arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (id.Length > 256 || id.Any(char.IsControl)) throw new ArgumentException("Invalid request id.", nameof(id));
        if (method.Length > 256 || method.Any(char.IsControl)) throw new ArgumentException("Invalid method name.", nameof(method));
        if (arguments.Kind != PluginValueKind.Array) throw new ArgumentException("API arguments must be an array.", nameof(arguments));
        Id = id;
        Method = method;
        Arguments = arguments;
    }
    public string Id { get; }
    public string Method { get; }
    public PluginValue Arguments { get; }
}
