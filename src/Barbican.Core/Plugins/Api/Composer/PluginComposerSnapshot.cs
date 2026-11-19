// Mirrors: lib/plugin-types.ts:1470-1479; composer-registry.ts:66-86.
using System.Collections.Immutable;
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>A detached coherent host snapshot; account and identity remain host-only.</summary>
public sealed record PluginComposerSnapshot
{
    public PluginComposerSnapshot(string revision, string subject, string text, IEnumerable<string> to, IEnumerable<string> cc, IEnumerable<string> bcc, IEnumerable<PluginComposerAttachment> attachments, bool pending)
    {
        Revision = revision; Subject = subject; Text = text;
        To = to.ToImmutableArray(); Cc = cc.ToImmutableArray(); Bcc = bcc.ToImmutableArray();
        Attachments = attachments.ToImmutableArray(); Pending = pending;
    }
    public string Revision { get; }
    public string Subject { get; }
    public string Text { get; }
    public ImmutableArray<string> To { get; }
    public ImmutableArray<string> Cc { get; }
    public ImmutableArray<string> Bcc { get; }
    public ImmutableArray<PluginComposerAttachment> Attachments { get; }
    public bool Pending { get; }
}
