namespace Barbican.Core.Net;

internal sealed record PluginFetchRequest(Uri Uri, HttpMethod Method, IReadOnlyDictionary<string, string> Headers, byte[]? Body)
{
    internal Func<bool>? CanConnect { get; init; }
}
