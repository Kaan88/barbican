// Mirrors: lib/plugin-types.ts:1470-1479; composer-registry.ts:66-86.
namespace Barbican.Core.Plugins.Api.Composer;
public sealed record PluginComposerSnapshot(string Revision, string Subject, string Text, IReadOnlyList<string> To, IReadOnlyList<string> Cc, IReadOnlyList<string> Bcc, IReadOnlyList<PluginComposerAttachment> Attachments, bool Pending);
