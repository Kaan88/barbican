// Mirrors: Bulwark protocol.ts ApiRequestMsg. There are deliberately no claimed authority, profile or frame fields.
using System.Text.Json;
namespace Barbican.Core.Plugins.Bridge;
public sealed class PluginApiRequest
{
    public PluginApiRequest(string id, string method, JsonElement arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (id.Length > 256 || id.Any(char.IsControl)) throw new ArgumentException("Invalid request id.", nameof(id));
        if (method.Length > 256 || method.Any(char.IsControl)) throw new ArgumentException("Invalid method name.", nameof(method));
        if (arguments.ValueKind != JsonValueKind.Array) throw new ArgumentException("API arguments must be an array.", nameof(arguments));
        Id = id;
        Method = method;
        Arguments = arguments.Clone();
    }
    public string Id { get; }
    public string Method { get; }
    public JsonElement Arguments { get; }
}
