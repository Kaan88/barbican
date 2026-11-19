// Mirrors: lib/plugin-sandbox/composer-registry.ts:24-37,62-96; host-api-composer.ts:107-143.
namespace Barbican.Core.Plugins.Api.Composer;
/// <summary>A host-issued, revocable capability. Account ownership never crosses the plugin boundary.</summary>
public interface IPluginComposerHandle
{
    string LocalAccountKey { get; }
    Task<PluginComposerSnapshot> GetSnapshotAsync(CancellationToken ct);
    Task<bool> SetSubjectAsync(string subject, string hostRevision, CancellationToken ct);
    Task<PluginComposerAttachmentData> ReadAttachmentAsync(string attachmentId, CancellationToken ct);
}
