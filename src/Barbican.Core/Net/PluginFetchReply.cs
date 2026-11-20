namespace Barbican.Core.Net;

internal sealed record PluginFetchReply(int Status, string StatusText, IReadOnlyDictionary<string, string> Headers, byte[] Body, Uri? Location = null);
