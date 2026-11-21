// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:62-96 doFiltersList (filters:read, host-api.ts c.ts:27,88) and c.types.ts:79-98 (FilterRuleInfo, FiltersListView): { supported, editable, rules }, a rule as
// { id, name, isActive, editable, match, conditions: [{ field, op, value, headerName? }], actions: [{ type, mailboxId?, folder? | tagId? | value? }] }. Without filters on the server: supported false, editable false, no rules;
// a script edited by hand (Locked here, opaque in Bulwark): supported, not editable, no rules. A rule written elsewhere (origin other than bulwark) is listed with editable false. Fields are named one by one; the rest of the rule never
// reaches the add-on. Read through IFilterService, the service Settings > Filters uses (Stalwart crates/jmap/src/sieve/get.rs).
using System.Text.Json.Nodes;
using Barbican.Core.Filters.Rules;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Plugins.Api.Account;
using Barbican.Core.Plugins.Bridge;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginFiltersList(IFilterService filters) : PluginScopedMethod("filters.list", "filters:read")
{
    private const string KeywordPrefix = "$label:";
    private const string LegacyKeywordPrefix = "$color:";

    public override PluginErrorCode? ValidateArguments(PluginValue arguments) => null;

    protected override async Task<PluginCallOutcome> RunAsync(IPluginInvocationContext context, PluginSessionCapture? capture, PluginValue arguments, CancellationToken ct)
    {
        var b = new PluginValueBuilder();
        if (!PluginRulesAccess.SieveAvailable(capture!.Session)) return Success(b, View(b, false, false, []));
        var loaded = await filters.LoadAsync(capture.LocalAccountKey, ct).ConfigureAwait(false);
        if (!loaded.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (loaded.Value!.Locked) return Success(b, View(b, true, false, []));
        var boxes = await FilterAccess.MailboxesAsync(capture.Session.Client, ct).ConfigureAwait(false);
        if (!boxes.IsOk) return PluginCallOutcome.Failed(PluginErrorCode.Failed);
        if (!await context.CheckCurrentAsync(ct).ConfigureAwait(false)) return Stale(context);
        var paths = FilterRules.FolderPaths(boxes.Value!).ToDictionary(f => f.Id, f => f.Path, StringComparer.Ordinal);
        return Success(b, View(b, true, true, [.. loaded.Value.Rules.Select(r => Rule(b, r, paths))]));
    }

    private static int View(PluginValueBuilder b, bool supported, bool editable, int[] rules) =>
        b.Object(("supported", b.Boolean(supported)), ("editable", b.Boolean(editable)), ("rules", b.Array(rules)));

    private static int Rule(PluginValueBuilder b, JsonNode rule, Dictionary<string, string> paths)
    {
        var origin = rule.Str("origin");
        var conditions = (rule["conditions"] as JsonArray ?? []).OfType<JsonObject>().Select(c => Condition(b, c));
        var actions = (rule["actions"] as JsonArray ?? []).OfType<JsonObject>().Select(a => Action(b, a, paths));
        return b.Object(
            ("id", b.String(rule.Str("id") ?? "")),
            ("name", b.String(rule.Str("name") ?? "")),
            ("isActive", b.Boolean(rule["enabled"] is not JsonValue e || !e.TryGetValue<bool>(out var on) || on)),
            ("editable", b.Boolean(origin is null || string.Equals(origin, "bulwark", StringComparison.Ordinal))),
            ("match", b.String(string.Equals(rule.Str("matchType"), "any", StringComparison.Ordinal) ? "any" : "all")),
            ("conditions", b.Array([.. conditions])),
            ("actions", b.Array([.. actions])));
    }

    private static int Condition(PluginValueBuilder b, JsonObject c)
    {
        var value = c["value"] is JsonArray list
            ? b.Array([.. list.Select(v => b.String(v.AsString() ?? ""))])
            : b.String(c["value"].AsString() ?? "");
        var fields = new List<(string, int)> { ("field", b.String(c.Str("field") ?? "")), ("op", b.String(c.Str("comparator") ?? "")), ("value", value) };
        if (c.Str("headerName") is { Length: > 0 } header) fields.Add(("headerName", b.String(header)));
        return b.Object([.. fields]);
    }

    private static int Action(PluginValueBuilder b, JsonObject a, Dictionary<string, string> paths)
    {
        var type = a.Str("type") ?? "";
        var fields = new List<(string, int)> { ("type", b.String(type)) };
        if (type is "move" or "copy")
        {
            var mailbox = a.Str("mailboxId");
            if (mailbox is not null) fields.Add(("mailboxId", b.String(mailbox)));
            fields.Add(("folder", b.String(mailbox is not null && paths.TryGetValue(mailbox, out var path) ? path : a.Str("value") ?? "")));
        }
        else if (string.Equals(type, "add_label", StringComparison.Ordinal))
        {
            fields.Add(("tagId", b.String(TagId(a.Str("value")))));
        }
        else if (string.Equals(type, "forward", StringComparison.Ordinal))
        {
            fields.Add(("value", b.String(a.Str("value") ?? "")));
        }

        return b.Object([.. fields]);
    }

    private static string TagId(string? keyword) =>
        keyword is null ? "" : keyword.StartsWith(KeywordPrefix, StringComparison.Ordinal) ? keyword[KeywordPrefix.Length..]
        : keyword.StartsWith(LegacyKeywordPrefix, StringComparison.Ordinal) ? keyword[LegacyKeywordPrefix.Length..] : keyword;
}
