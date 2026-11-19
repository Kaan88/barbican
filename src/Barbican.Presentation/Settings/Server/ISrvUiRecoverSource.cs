// Mirrors: Bulwark components/settings/recover-deleted-settings.tsx, lib/recovery/archived-items.ts; Rust server_settings.rs:137-152; I-438. The data side is Core's ArchivedItems (static, over an IJmapClient); this interface lets the page and the sample-data run share one shape. Owner srv-ui.

using Barbican.Core.Common;
using Barbican.Core.Mail.Archive;

namespace Barbican.Presentation.Settings.Server;

/// <summary>The deleted items the server still keeps, and restoring one.</summary>
public interface ISrvUiRecoverSource
{
    /// <summary>The server offers recovery (Stalwart); the page says so when not.</summary>
    bool Available(string accountKey);

    Task<Result<IReadOnlyList<ArchivedItem>>> ListAsync(string accountKey, CancellationToken ct);

    Task<Result<Unit>> RestoreAsync(string accountKey, string id, CancellationToken ct);
}
