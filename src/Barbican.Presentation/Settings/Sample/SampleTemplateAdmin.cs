// Mirrors: M6 plan section 3.5 (D-M6-00): an in-memory fake of ITemplateAdmin that behaves like the real one where the M5 sample stores did not: its calls complete off the caller's thread, and its Changed is raised from a pool thread, so a view built on it meets the same threading the real service has (M5 found two such bugs, the real store loading on the UI thread and raising from the pool).
// Frozen in the M6 plan commit; owner integrator. Used by view-model tests and by sample-data runs (BARBICAN_SAMPLE=1); the data stream's real class never uses it.

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Compose;
using Barbican.Core.Settings.Templates;

namespace Barbican.Presentation.Settings.Sample;

/// <summary>An in-memory <see cref="ITemplateAdmin"/>.</summary>
public sealed class SampleTemplateAdmin : ITemplateAdmin
{
    private readonly Lock _lock = new();
    private readonly List<MailTemplate> _items = [new("t1", "Thanks", "Thank you", "Hi {{name}}, thank you.", false)];
    private int _next = 2;

    public event EventHandler<TemplateAdminChangedEventArgs>? Changed;

    public Task<Result<IReadOnlyList<MailTemplate>>> ListAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                return Result<IReadOnlyList<MailTemplate>>.Ok([.. _items.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)]);
            }
        });

    public async Task<Result<MailTemplate>> CreateAsync(string accountKey, string name, string subject, string body, bool isHtml, CancellationToken ct)
    {
        var made = await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                var t = new MailTemplate("t" + _next++, name, subject, body, isHtml);
                _items.Add(t);
                return t;
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey)));
        return Result<MailTemplate>.Ok(made);
    }

    public async Task<Result<MailTemplate>> UpdateAsync(string accountKey, MailTemplate edited, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(edited);
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                var at = _items.FindIndex(t => string.Equals(t.Id, edited.Id, StringComparison.Ordinal));
                if (at >= 0)
                {
                    _items[at] = edited;
                }
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey)));
        return Result<MailTemplate>.Ok(edited);
    }

    public async Task<Result<Unit>> DeleteAsync(string accountKey, string id, CancellationToken ct)
    {
        await SampleThreading.OffThreadAsync(() =>
        {
            lock (_lock)
            {
                _ = _items.RemoveAll(t => string.Equals(t.Id, id, StringComparison.Ordinal));
            }
        }).ConfigureAwait(false);
        SampleThreading.RaiseOnPool(() => Changed?.Invoke(this, new TemplateAdminChangedEventArgs(accountKey)));
        return Result<Unit>.Ok(default);
    }

    public Task<Result<JsonObject>> ExportAsync(string accountKey, CancellationToken ct) =>
        SampleThreading.OffThreadAsync(() => Result<JsonObject>.Ok(new JsonObject { ["templates"] = new JsonArray() }));

    public Task<Result<int>> ImportAsync(string accountKey, JsonNode document, CancellationToken ct) => SampleThreading.OffThreadAsync(() => Result<int>.Ok(0));
}
