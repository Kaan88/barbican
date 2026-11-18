// Mirrors: Rust templates.rs; Bulwark stores/template-store.ts, stores/account-templates-store.ts, components/settings/template-settings.tsx (edit, rich body, import and export, sync), lib/template-placeholders; Core/Compose/TemplateStore.cs (M3: list, create, delete in the Inbox metadata, tombstones, ifInState). G-032..G-035, I-289, I-290, I-425. Contract from the M6 plan section 4.8; owner srv-data.

// Hooks onTemplateCreate, onTemplateUpdate (intercept), onTemplateDelete, onTemplatesImport, and the composer-side onTemplateApply and onTemplateRender (call sites in the composer's template insert, the one allowed edit in the Compose files, D-M6-C04).
// THREADING (D-M6-00): completes off the UI thread; Changed is raised on a pool thread. Contract from the M6 plan section 4.8; owner srv-data.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Core.Settings.Templates;

/// <summary>The templates manager (the composer keeps ITemplateStore for insert; this adds edit, import, export and the hooks).</summary>
public interface ITemplateAdmin
{
    Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountKey, CancellationToken ct);

    Task<Result<MailTemplate>> CreateAsync(string accountKey, string name, string subject, string body, bool isHtml, CancellationToken ct);

    /// <summary>Edits an existing template (name, subject, body, rich or plain) in the Inbox metadata, with ifInState.</summary>
    Task<Result<MailTemplate>> UpdateAsync(string accountKey, MailTemplate edited, CancellationToken ct);

    Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct);

    /// <summary>The Bulwark export document of the account's templates.</summary>
    Task<Result<JsonObject>> ExportAsync(string accountKey, CancellationToken ct);

    /// <summary>Imports a Bulwark export (or a plain array of templates); returns how many were created, names that exist get " (2)".</summary>
    Task<Result<int>> ImportAsync(string accountKey, JsonNode document, CancellationToken ct);

    event EventHandler<TemplateAdminChangedEventArgs>? Changed;
}
