// Mirrors: Rust filters_x.rs (:92 conds_of, :99 acts_of, :116 build_rule, :198 summary), Bulwark filter-rule-modal.tsx. M6 plan section 4.6. srv-data owns FilterRules, which is a stub until its stream merges: every call here asks FilterRules first and, while it is still pending,
// falls back to a plain local reading of the rule JSON (no validation beyond "a condition and an action"), so the page and the sample-data run work before the merge. After the merge the fallbacks are never reached; the integrator may delete them (decision D-M6-D02).

using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Filters.Rules;

namespace Barbican.Presentation.Settings.Server;

/// <summary>Calls into <see cref="FilterRules"/> with a fallback while srv-data's bodies are pending.</summary>
public static class SrvUiFilterBridge
{
    /// <summary>The action kinds and labels (reject only when advertised).</summary>
    public static IReadOnlyList<(string Id, string Label)> ActionKinds(IReadOnlyList<string> extensions, bool rejectAdvertised)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        try
        {
            return FilterRules.ActionKinds(extensions);
        }
        catch (PortPendingException)
        {
            var all = new List<(string, string)> { ("move", "Move to folder"), ("copy", "Copy to folder"), ("forward", "Forward to"), ("mark_read", "Mark as read"), ("star", "Star message"), ("add_label", "Add tag"), ("discard", "Discard"), ("keep", "Keep in inbox") };
            if (rejectAdvertised || extensions.Contains("reject", StringComparer.Ordinal))
            {
                all.Insert(7, ("reject", "Reject with message"));
            }

            return all;
        }
    }

    public static IReadOnlyList<FilterCond> Conditions(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        try
        {
            return FilterRules.ConditionsOf(rule);
        }
        catch (PortPendingException)
        {
            return [.. (rule["conditions"] as JsonArray ?? []).OfType<JsonObject>().Select(c => new FilterCond(Str(c, "field"), Str(c, "comparator"), ValueText(c["value"]), Str(c, "headerName")))];
        }
    }

    public static IReadOnlyList<FilterAct> Actions(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        try
        {
            return FilterRules.ActionsOf(rule);
        }
        catch (PortPendingException)
        {
            return [.. (rule["actions"] as JsonArray ?? []).OfType<JsonObject>().Select(a => new FilterAct(Str(a, "type"), Str(a, "value"), Str(a, "mailboxId"), a["keepCopy"]?.GetValue<bool>() ?? false))];
        }
    }

    public static string Summary(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        try
        {
            return FilterRules.Summary(rule);
        }
        catch (PortPendingException)
        {
            var join = string.Equals(Str(rule, "matchType"), "any", StringComparison.Ordinal) ? " or " : " and ";
            var conds = string.Join(join, Conditions(rule).Select(c => (c.Field + " " + c.Comparator.Replace('_', ' ') + " " + c.Value).Trim()));
            var acts = string.Join(", ", Actions(rule).Select(a => (a.Kind.Replace('_', ' ') + " " + a.Value).Trim()));
            return conds + " → " + acts + (rule["stopProcessing"]?.GetValue<bool>() == true ? " · then stop" : string.Empty);
        }
    }

    public static IReadOnlyList<JsonNode> Moved(IReadOnlyList<JsonNode> rules, string id, int direction)
    {
        ArgumentNullException.ThrowIfNull(rules);
        try
        {
            return FilterRules.Moved(rules, id, direction);
        }
        catch (PortPendingException)
        {
            var list = rules.ToList();
            var at = list.FindIndex(r => string.Equals(Str(r, "id"), id, StringComparison.Ordinal));
            var to = at + direction;
            if (at < 0 || to < 0 || to >= list.Count)
            {
                return list;
            }

            (list[at], list[to]) = (list[to], list[at]);
            return list;
        }
    }

    public static string? RetroUnsupported(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        try
        {
            return FilterRules.RetroUnsupported(rule);
        }
        catch (PortPendingException)
        {
            return null;
        }
    }

    public static string AppliedText(FilterApplied result, int total)
    {
        ArgumentNullException.ThrowIfNull(result);
        try
        {
            return FilterRules.AppliedText(result, total);
        }
        catch (PortPendingException)
        {
            return result.Kind switch
            {
                FilterAppliedKind.Done => "Rule applied to " + result.Count + " message" + (result.Count == 1 ? string.Empty : "s"),
                FilterAppliedKind.Stale => "Applied to " + result.Count + " of " + total + " messages; your mail changed meanwhile, run the rule again for the rest.",
                _ => "Applied to " + result.Count + " of " + total + " messages: " + result.Error,
            };
        }
    }

    public static FilterBuilt Build(JsonNode? @base, string? id, string name, bool any, IReadOnlyList<FilterCond> conds, IReadOnlyList<FilterAct> acts, IReadOnlyList<FilterFolder> folders, bool stop, bool includeSpam, string from, string until)
    {
        ArgumentNullException.ThrowIfNull(conds);
        ArgumentNullException.ThrowIfNull(acts);
        try
        {
            return FilterRules.BuildRule(@base, id, name, any, conds, acts, folders, stop, includeSpam, from, until);
        }
        catch (PortPendingException)
        {
            if (conds.Count == 0 || acts.Count == 0)
            {
                return new FilterBuilt(null, "Add at least one condition and one action.");
            }

            var rule = @base?.DeepClone() as JsonObject ?? [];
            var kept = Str(rule, "id");
            rule["id"] = id ?? (kept.Length > 0 ? kept : "bw-" + Guid.NewGuid().ToString("N")[..12]);
            rule["name"] = string.IsNullOrWhiteSpace(name) ? "Rule" : name.Trim();
            rule["enabled"] ??= true;
            rule["matchType"] = any ? "any" : "all";
            rule["conditions"] = new JsonArray([.. conds.Select(c => (JsonNode)new JsonObject { ["field"] = c.Field, ["comparator"] = c.Comparator, ["value"] = c.Value, ["headerName"] = string.Equals(c.Field, "header", StringComparison.Ordinal) ? c.Header : null })]);
            rule["actions"] = new JsonArray([.. acts.Select(a => (JsonNode)new JsonObject { ["type"] = a.Kind, ["value"] = a.Value.Length > 0 ? a.Value : null, ["mailboxId"] = a.Mailbox.Length > 0 ? a.Mailbox : null, ["keepCopy"] = a.KeepCopy ? true : null })]);
            rule["stopProcessing"] = stop;
            _ = rule.Remove("includeSpam");
            if (includeSpam)
            {
                rule["includeSpam"] = true;
            }

            return new FilterBuilt(rule, null);
        }
    }

    /// <summary>The string property of a rule, or empty.</summary>
    public static string Str(JsonNode? node, string name) => node is JsonObject o && o[name] is JsonValue v && v.TryGetValue<string>(out var s) ? s : string.Empty;

    private static string ValueText(JsonNode? value) => value switch
    {
        JsonArray a => string.Join(", ", a.Select(x => x?.ToString() ?? string.Empty)),
        null => string.Empty,
        _ => value.ToString(),
    };
}
