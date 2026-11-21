// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1019-1025 doContactSearch and :181,2210 (contacts:read), lib/jmap/client.ts:8950-8995 searchContacts (a text query over every account the login has contacts in, at most 50 hits per account).
// Deviation: matching is done on the store the Contacts view shows (case, accents and spacing ignored, any text of the card except photos, keys and ids) instead of Stalwart's text filter; an empty query is refused.
using Barbican.Core.Contacts;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.Contacts;

internal sealed class PluginContactSearch(IPluginContactsHost host) : PluginContactsMethod("contact.search", "contacts:read", host, false)
{
    internal const int MaxQueryChars = 200;

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) =>
        PluginMailArgs.TryString(PluginMailArgs.At(arguments, 0), MaxQueryChars, out var query) && query.Trim().Length > 0 ? null : PluginErrorCode.InvalidArguments;

    protected override async Task<PluginCallOutcome> RunAsync(PluginContactsCall call, PluginValue arguments, CancellationToken ct)
    {
        var folded = ContactText.Fold(arguments.Item(0).StringValue.Trim());
        var snapshot = await PluginContactsData.SnapshotAsync(Host, call.AccountKey, ct).ConfigureAwait(false);
        if (snapshot is null) return Fail(PluginErrorCode.Unavailable);
        if (!await call.Context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Fail(PluginErrorCode.StaleContext);
        var hits = snapshot.Cards.Where(c => PluginContactsData.Matches(c, folded))
            .GroupBy(c => (c as System.Text.Json.Nodes.JsonObject)?["_acc"]?.ToString() ?? "", StringComparer.Ordinal)
            .SelectMany(g => g.Take(PluginContactsData.MaxSearchHitsPerAccount))
            .ToList();
        var b = new PluginValueBuilder();
        return Success(b, b.Array([.. hits.Select(c => PluginContactProjection.Card(b, c))]));
    }
}
