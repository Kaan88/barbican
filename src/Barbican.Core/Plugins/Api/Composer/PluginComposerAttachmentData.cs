// Mirrors: lib/plugin-types.ts:1482-1487; host-api-composer.ts:124-143.
namespace Barbican.Core.Plugins.Api.Composer;
public sealed record PluginComposerAttachmentData(string Name, string Type, ReadOnlyMemory<byte> Data);
