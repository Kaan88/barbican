// Test double for the Templates settings service: an in-memory template list per account, counting writes, with a hook before each call.
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Settings.Templates;

namespace Barbican.Core.Tests.Plugins.Api;

internal sealed class FakeTemplateAdmin : ITemplateAdmin
{
    private int _next = 1;

    public List<MailTemplate> Items { get; } = [];

    public int Writes { get; private set; }

    public Action? Hook { get; set; }

    public string? LastAccountKey { get; private set; }

    public event EventHandler<TemplateAdminChangedEventArgs>? Changed { add { } remove { } }

    public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountKey, CancellationToken ct)
    {
        Hook?.Invoke();
        LastAccountKey = accountKey;
        return Task.FromResult(Result<IReadOnlyList<MailTemplate>>.Ok([.. Items]));
    }

    public Task<Result<MailTemplate>> CreateAsync(string accountKey, string name, string subject, string body, bool isHtml, CancellationToken ct)
    {
        Hook?.Invoke();
        Writes++;
        var template = new MailTemplate("t" + _next++, name, subject, body, isHtml);
        Items.Add(template);
        return Task.FromResult(Result<MailTemplate>.Ok(template));
    }

    public Task<Result<MailTemplate>> UpdateAsync(string accountKey, MailTemplate edited, CancellationToken ct)
    {
        Hook?.Invoke();
        Writes++;
        Items[Items.FindIndex(t => string.Equals(t.Id, edited.Id, StringComparison.Ordinal))] = edited;
        return Task.FromResult(Result<MailTemplate>.Ok(edited));
    }

    public Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct)
    {
        Hook?.Invoke();
        Writes++;
        Items.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));
        return Task.FromResult(Result<Unit>.Ok(default));
    }

    public Task<Result<JsonObject>> ExportAsync(string accountKey, CancellationToken ct) => throw new NotSupportedException();

    public Task<Result<int>> ImportAsync(string accountKey, JsonNode document, CancellationToken ct) => throw new NotSupportedException();
}
