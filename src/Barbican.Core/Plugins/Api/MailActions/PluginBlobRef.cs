// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:156-182 readBlobRefs (blobId, name, type, size of a blob already on the server).
namespace Barbican.Core.Plugins.Api.MailActions;

/// <summary>A blob an add-on says is already on the server and wants on a message.</summary>
public sealed record PluginBlobRef(string BlobId, string Name, string Type, long Size);
