// Mirrors: see ITemplateAdmin. Stub from the M6 plan section 4.8; owner srv-data replaces the class (constructor dependencies: ITemplateStore, IHookBus).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;

namespace Barbican.Core.Settings.Templates;

/// <summary>The default <see cref="ITemplateAdmin"/>.</summary>
public sealed class TemplateAdmin : ITemplateAdmin
{
    public event EventHandler<TemplateAdminChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("TemplateAdmin.ListAsync");

    public Task<Result<MailTemplate>> CreateAsync(string accountKey, string name, string subject, string body, bool isHtml, CancellationToken ct) => throw new PortPendingException("TemplateAdmin.CreateAsync");

    public Task<Result<MailTemplate>> UpdateAsync(string accountKey, MailTemplate edited, CancellationToken ct) => throw new PortPendingException("TemplateAdmin.UpdateAsync");

    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct) => throw new PortPendingException("TemplateAdmin.DeleteAsync");

    public Task<Result<JsonObject>> ExportAsync(string accountKey, CancellationToken ct) => throw new PortPendingException("TemplateAdmin.ExportAsync");

    public Task<Result<int>> ImportAsync(string accountKey, JsonNode document, CancellationToken ct) => throw new PortPendingException("TemplateAdmin.ImportAsync");
}
