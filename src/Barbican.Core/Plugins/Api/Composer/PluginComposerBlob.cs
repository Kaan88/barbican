// Mirrors: lib/plugin-sandbox/host-api-composer.ts:132-146 (composer.attach file: blobId, name, type, size).
namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>An already uploaded blob a plugin asks the composer to attach.</summary>
public sealed record PluginComposerBlob(string BlobId, string Name, string Type, long Size);
