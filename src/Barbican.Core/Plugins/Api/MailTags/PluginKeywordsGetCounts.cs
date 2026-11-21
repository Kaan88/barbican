// Mirrors: Bulwark lib/plugin-sandbox/host-api.ts:1822-1838 doKeywordsGetCounts (email:read, host-api.ts:248 - counts reveal mail metadata, so not a settings
// permission): { [tagId]: { total, unread } } from what was last counted; ids (at most 500 valid tag ids, or null/absent for all) selects, unknown ids are left out.
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailTags;

internal sealed class PluginKeywordsGetCounts(PluginKeywordCounts counts) : PluginScopedMethod("keywords.getCounts", "email:read")
{
    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => TryIds(arguments, out _) ? null : PluginErrorCode.InvalidArguments;

    private static bool TryIds(PluginValue arguments, out List<string>? ids)
    {
        ids = null;
        var value = PluginKeywordsAdd.Arg(arguments);
        if (value is null || value.Kind is PluginValueKind.Null or PluginValueKind.Undefined) return true;
        var ok = PluginKeywordArgs.TryIds(value, strict: true, out var list);
        ids = list;
        return ok;
    }

    protected override Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        _ = TryIds(arguments, out var ids);
        var b = new PluginValueBuilder();
        return Task.FromResult(Success(b, Project(b, counts.Get(capture!.LocalAccountKey), ids)));
    }

    internal static int Project(PluginValueBuilder b, IReadOnlyDictionary<string, (int Total, int Unread)> all, IReadOnlyList<string>? ids)
    {
        var chosen = ids is null ? all.Keys : ids.Where(all.ContainsKey).Distinct(StringComparer.Ordinal);
        return b.Object([.. chosen.Select(id => (id, b.Object(("total", b.Number(all[id].Total)), ("unread", b.Number(all[id].Unread)))))]);
    }
}
