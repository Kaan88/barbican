// Mirrors: Rust templates.rs list/create/delete; Bulwark lib/jmap/client.ts template metadata calls, stores/template-store.ts; Stalwart crates/jmap/src/mailbox/set.rs (metadata, ifInState). Contract from the M3 plan section 4.3; owner send-pipeline.

using Barbican.Core.Common;

namespace Barbican.Core.Compose;

/// <summary>Templates in the Inbox's JMAP metadata. Delete writes a tombstone with <c>ifInState</c> and retries three times on stateMismatch.</summary>
public interface ITemplateStore
{
    /// <summary>All personal templates sorted by name (case-insensitive); empty when the server has no metadata capability.</summary>
    Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountId, CancellationToken ct);

    Task<Result<MailTemplate>> CreateAsync(string accountId, string name, string subject, string body, bool isHtml, CancellationToken ct);

    Task<Result<Unit>> DeleteAsync(string accountId, string templateId, CancellationToken ct);
}
