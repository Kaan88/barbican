// Mirrors: Bulwark lib/plugin-sandbox/assistant/c.filters.ts:20-30,140-200 (FILTER_ACTIONS, readAction, readFilterRule): name 1-100, match all or any, 1-10 conditions, 1-10 actions, and nothing that sends mail elsewhere or
// drops it: no forward, redirect, reject, discard or keep (each refused by name, c.filters.ts:144-149). "stop" is not an action of the stored rule but its stopProcessing flag, and a rule must do something besides stop.
// Folders and tags are looked up later, against the account (PluginFiltersCreate).
using System.Text.Json.Nodes;
using Barbican.Core.Plugins.Api.Mail;
using Barbican.Core.Plugins.Protocol;

namespace Barbican.Core.Plugins.Api.MailRules;

internal sealed class PluginFilterRuleInput
{
    internal const int MaxConditions = 10;
    internal const int MaxActions = 10;
    internal const int MaxName = 100;
    private static readonly string[] RuleKeys = ["name", "match", "conditions", "actions"];
    private static readonly string[] ActionKeys = ["type", "mailboxId", "tagId"];
    private static readonly string[] ActionTypes = ["move", "copy", "mark_read", "star", "add_label", "stop"];

    private PluginFilterRuleInput(string name, bool any, List<JsonObject> conditions, List<PluginFilterAction> actions, bool stop)
    {
        Name = name;
        Any = any;
        Conditions = conditions;
        Actions = actions;
        Stop = stop;
    }

    internal string Name { get; }

    internal bool Any { get; }

    internal IReadOnlyList<JsonObject> Conditions { get; }

    /// <summary>The actions without "stop".</summary>
    internal IReadOnlyList<PluginFilterAction> Actions { get; }

    internal bool Stop { get; }

    internal static PluginFilterRuleInput? TryRead(PluginValue? input)
    {
        if (!PluginMailArgs.TryOptions(input, RuleKeys, true, out var data) || data is null) return null;
        if (PluginMailArgs.Prop(data, "name") is not { Kind: PluginValueKind.String } nameValue || nameValue.StringValue.Trim() is not { Length: > 0 and <= MaxName } name) return null;
        var any = false;
        if (PluginMailArgs.Prop(data, "match") is { } match)
        {
            if (match.Kind != PluginValueKind.String || match.StringValue is not ("all" or "any")) return null;
            any = string.Equals(match.StringValue, "any", StringComparison.Ordinal);
        }

        if (PluginMailArgs.Prop(data, "conditions") is not { Kind: PluginValueKind.Array, Count: > 0 and <= MaxConditions } rawConditions) return null;
        if (PluginMailArgs.Prop(data, "actions") is not { Kind: PluginValueKind.Array, Count: > 0 and <= MaxActions } rawActions) return null;
        var conditions = new List<JsonObject>();
        for (var i = 0; i < rawConditions.Count; i++)
        {
            if (PluginFilterCondition.TryRead(rawConditions.Item(i)) is not { } condition) return null;
            conditions.Add(condition);
        }

        var actions = new List<PluginFilterAction>();
        var stop = false;
        for (var i = 0; i < rawActions.Count; i++)
        {
            if (ReadAction(rawActions.Item(i)) is not { } action) return null;
            if (string.Equals(action.Type, "stop", StringComparison.Ordinal)) stop = true;
            else actions.Add(action);
        }

        return actions.Count == 0 ? null : new PluginFilterRuleInput(name, any, conditions, actions, stop);
    }

    /// <summary>The rule as the filters store it (an enabled rule with a fresh id), from the folders and tags looked up.</summary>
    internal JsonObject ToRule(string id, IReadOnlyDictionary<string, string> folderPaths)
    {
        var actions = new JsonArray();
        foreach (var action in Actions)
        {
            JsonNode node = new JsonObject { ["type"] = action.Type };
            if (action.MailboxId is { } mailbox)
            {
                node["value"] = folderPaths[mailbox];
                node["mailboxId"] = mailbox;
            }
            else if (action.TagId is { } tag)
            {
                node["value"] = tag;
            }

            actions.Add(node);
        }

        return new JsonObject
        {
            ["id"] = id,
            ["name"] = Name,
            ["enabled"] = true,
            ["matchType"] = Any ? "any" : "all",
            ["conditions"] = new JsonArray([.. Conditions.Select(c => (JsonNode?)c.DeepClone())]),
            ["actions"] = actions,
            ["stopProcessing"] = Stop,
        };
    }

    private static PluginFilterAction? ReadAction(PluginValue entry)
    {
        if (!PluginMailArgs.TryOptions(entry, ActionKeys, true, out var a) || a is null) return null;
        if (PluginMailArgs.Prop(a, "type") is not { Kind: PluginValueKind.String } typeValue || !ActionTypes.Contains(typeValue.StringValue, StringComparer.Ordinal)) return null;
        var type = typeValue.StringValue;
        var mailbox = PluginMailArgs.Prop(a, "mailboxId");
        var tag = PluginMailArgs.Prop(a, "tagId");
        if (type is "move" or "copy")
        {
            return PluginMailArgs.IsId(mailbox) && tag is null ? new PluginFilterAction(type, mailbox!.StringValue, null) : null;
        }

        if (mailbox is not null) return null;
        if (string.Equals(type, "add_label", StringComparison.Ordinal))
        {
            return tag is { Kind: PluginValueKind.String } && tag.StringValue.Trim() is { Length: > 0 and <= 100 } id ? new PluginFilterAction(type, null, id) : null;
        }

        return tag is null ? new PluginFilterAction(type, null, null) : null;
    }
}
