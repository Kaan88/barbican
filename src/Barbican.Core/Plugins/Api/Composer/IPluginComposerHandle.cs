// Mirrors: lib/plugin-sandbox/composer-registry.ts:24-37,62-96; host-api-composer.ts:107-146; host-api-checker.ts:78-100; composer-edit.ts.
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Composer;

/// <summary>A host-issued, revocable capability. Account ownership never crosses the plugin boundary. Every call runs on the editor's thread inside the host.</summary>
public interface IPluginComposerHandle
{
    string LocalAccountKey { get; }
    Task<PluginComposerSnapshot> GetSnapshotAsync(CancellationToken ct);
    Task<bool> SetSubjectAsync(string subject, string hostRevision, CancellationToken ct);
    Task<PluginComposerAttachmentData> ReadAttachmentAsync(string attachmentId, CancellationToken ct);

    /// <summary>The user's own text, never the signature or the quoted original.</summary>
    Task<PluginComposerBody> ReadBodyAsync(CancellationToken ct);
    Task<PluginComposerSelection> ReadSelectionAsync(CancellationToken ct);

    /// <summary>One editor undo step. False when nothing was changed (revision moved on, protected text, editor gone).</summary>
    Task<bool> ReplaceBodyAsync(PluginComposerContent content, string hostRevision, CancellationToken ct);
    Task<bool> ReplaceSelectionAsync(PluginComposerContent content, bool below, string hostRevision, CancellationToken ct);

    /// <summary>Adds an uploaded blob as a chip, after the account's size limits.</summary>
    Task<bool> AttachAsync(PluginComposerBlob blob, string hostRevision, CancellationToken ct);

    /// <summary>The whole checkable text for an on-demand checker, or null when there is nothing to check.</summary>
    Task<PluginCheckRequest?> BeginCheckAsync(PluginCheckerRegistration spec, CancellationToken ct);

    /// <summary>Replaces this checker's annotations; returns how many were kept (0 when the revision or the text moved on).</summary>
    Task<int> SetAnnotationsAsync(PluginCheckerRegistration spec, string revision, PluginValue annotations, CancellationToken ct);
}
