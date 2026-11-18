// Mirrors: Rust filters_x.rs: :17 FIELDS, :20 CMP_LABELS, :26 comparators, :40 ACTS, :73 folder_paths, :92 conds_of, :99 acts_of, :116 build_rule, :198 summary, :220 moved (the retroactive half is in FilterRules.Retro.cs, the apply half in FilterRules.Apply.cs);
// Bulwark components/settings/filter-rule-modal.tsx (COMPARATORS_BY_FIELD, the form's validation), lib/sieve/generator.ts:29 isValidSieveHeaderName. T-093..T-095, T-100, T-101, G-020. Contract from the M6 plan section 4.6; owner srv-data.
// Rules are JsonNode (Bulwark's FilterRule JSON; unknown properties survive a rebuild, D-M6-C01).

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Barbican.Core.Sieve;

namespace Barbican.Core.Filters.Rules;

/// <summary>The pure rules of the filter editor and of "apply to existing mail".</summary>
public static partial class FilterRules
{
    private static readonly string[] AddressComparators = ["contains", "not_contains", "is", "not_is", "starts_with", "ends_with", "matches", "regex", "address_is", "domain_is"];
    private static readonly string[] TextComparators = ["contains", "not_contains", "is", "not_is", "starts_with", "ends_with", "matches", "regex"];
    private static readonly string[] BodyComparators = ["contains", "is", "regex"];
    private static readonly string[] SizeComparators = ["greater_than", "less_than"];
    private static readonly string[] AttachmentComparators = ["has_any", "has_type"];

    // Rust filters_x.rs:20 CMP_LABELS
    private static readonly Dictionary<string, string> ComparatorLabels = new(StringComparer.Ordinal)
    {
        ["contains"] = "contains",
        ["not_contains"] = "does not contain",
        ["is"] = "is exactly",
        ["not_is"] = "is not",
        ["starts_with"] = "starts with",
        ["ends_with"] = "ends with",
        ["matches"] = "matches pattern",
        ["regex"] = "matches regex",
        ["address_is"] = "is the address",
        ["domain_is"] = "has the domain",
        ["greater_than"] = "is greater than",
        ["less_than"] = "is less than",
        ["has_any"] = "is present",
        ["has_type"] = "of type",
        ["any"] = string.Empty,
    };

    // Rust filters_x.rs:40 ACTS: (type, label, what it needs)
    private static readonly (string Id, string Label, string Needs)[] Actions =
    [
        ("move", "Move to folder", "folder"),
        ("copy", "Copy to folder", "folder"),
        ("forward", "Forward to", "address"),
        ("mark_read", "Mark as read", string.Empty),
        ("star", "Star message", string.Empty),
        ("add_label", "Add tag", "tag"),
        ("discard", "Discard (delete silently)", string.Empty),
        ("reject", "Reject with message", "text"),
        ("keep", "Keep in inbox", string.Empty),
        ("stop", "Don't apply later rules", string.Empty),
    ];

    /// <summary>The nine condition fields with their labels (id, label). Rust filters_x.rs:17.</summary>
    public static IReadOnlyList<(string Id, string Label)> Fields { get; } =
    [
        ("from", "From"), ("to", "To"), ("cc", "Cc"), ("subject", "Subject"), ("header", "Custom header"),
        ("size", "Size"), ("body", "Body"), ("attachment", "Attachment"), ("all", "All messages"),
    ];

    /// <summary>The comparators a field allows (Bulwark COMPARATORS_BY_FIELD): from, to, cc ten; subject and header eight; body contains, is, regex; size greater_than and less_than; attachment has_any and has_type; all none.</summary>
    public static IReadOnlyList<string> Comparators(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        return field switch
        {
            "from" or "to" or "cc" => AddressComparators,
            "subject" or "header" => TextComparators,
            "body" => BodyComparators,
            "size" => SizeComparators,
            "attachment" => AttachmentComparators,
            _ => [],
        };
    }

    /// <summary>The label of a comparator in the summary and the editor ("does not contain").</summary>
    public static string ComparatorLabel(string comparator) => ComparatorLabels.TryGetValue(comparator ?? string.Empty, out var l) ? l : string.Empty;

    /// <summary>The action kinds the editor offers, with labels (id, label); <c>reject</c> only when <paramref name="serverExtensions"/> lists "reject" (G-020, B-SIEVE-030).</summary>
    public static IReadOnlyList<(string Id, string Label)> ActionKinds(IReadOnlyList<string> serverExtensions)
    {
        ArgumentNullException.ThrowIfNull(serverExtensions);
        var reject = serverExtensions.Contains("reject", StringComparer.Ordinal);
        return [.. Actions.Where(a => reject || !string.Equals(a.Id, "reject", StringComparison.Ordinal)).Select(a => (a.Id, a.Label))];
    }

    /// <summary>The folders as Sieve's fileinto names them ("Parent/Child", the Inbox as "INBOX"), the Inbox first then by path (Rust filters_x.rs:73 folder_paths); <paramref name="mailboxes"/> are Mailbox/get objects (id, name, parentId, role).</summary>
    public static IReadOnlyList<FilterFolder> FolderPaths(IReadOnlyList<JsonNode> mailboxes)
    {
        ArgumentNullException.ThrowIfNull(mailboxes);
        var byId = mailboxes.Where(b => Str(b, "id") is not null).ToDictionary(b => Str(b, "id")!, StringComparer.Ordinal);
        var list = new List<FilterFolder>();
        foreach (var b in mailboxes)
        {
            var parts = new List<string> { Str(b, "name") ?? string.Empty };
            var parent = Str(b, "parentId");
            for (var guard = 0; parent is not null && guard <= 20 && byId.TryGetValue(parent, out var pb); guard++)
            {
                parts.Add(Str(pb, "name") ?? string.Empty);
                parent = Str(pb, "parentId");
            }

            parts.Reverse();
            list.Add(new FilterFolder(Str(b, "id") ?? string.Empty, string.Equals(Str(b, "role"), "inbox", StringComparison.Ordinal) ? "INBOX" : string.Join('/', parts)));
        }

        return [.. list.OrderBy(f => !string.Equals(f.Path, "INBOX", StringComparison.Ordinal)).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The conditions of a rule as form rows (Rust filters_x.rs:92 conds_of).</summary>
    public static IReadOnlyList<FilterCond> ConditionsOf(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule["conditions"] is JsonArray a
            ? [.. a.Select(c => new FilterCond(Str(c, "field") ?? "from", Str(c, "comparator") ?? "contains", ValuesText(c?["value"]), Str(c, "headerName") ?? string.Empty))]
            : [];
    }

    /// <summary>The actions of a rule as form rows (Rust filters_x.rs:99 acts_of).</summary>
    public static IReadOnlyList<FilterAct> ActionsOf(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return rule["actions"] is JsonArray a
            ? [.. a.Select(x => new FilterAct(Str(x, "type") ?? "move", Str(x, "value") ?? string.Empty, Str(x, "mailboxId") ?? string.Empty, x?["keepCopy"] is JsonValue k && k.TryGetValue<bool>(out var b) && b))]
            : [];
    }

    /// <summary>The rule as Bulwark stores it, from the form, or what to fix (T-093, T-094, T-100, T-101): a condition and an action are required; a size is digits with one optional K, M or G (upper-cased); a regex must compile; a header name is letters, digits, "_" and "-" only; a move or copy needs a folder, a forward a valid address, reject a message; an until before the from is refused; the id is kept when editing, else a new "bw-" id; unknown properties of <paramref name="base"/> survive.</summary>
    public static FilterBuilt BuildRule(JsonNode? @base, string? id, string name, bool any, IReadOnlyList<FilterCond> conds, IReadOnlyList<FilterAct> acts, IReadOnlyList<FilterFolder> folders, bool stop, bool includeSpam, string from, string until)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(conds);
        ArgumentNullException.ThrowIfNull(acts);
        ArgumentNullException.ThrowIfNull(folders);
        if (conds.Count == 0)
        {
            return Bad("Add a condition.");
        }

        if (acts.Count == 0)
        {
            return Bad("At least one action is required");
        }

        var cj = new JsonArray();
        var aj = new JsonArray();
        foreach (var built in conds.Select(BuildCondition).Concat(acts.Select(a => BuildAction(a, folders))))
        {
            if (built.Error is not null)
            {
                return built;
            }

            (built.Rule!["field"] is not null ? cj : aj).Add(built.Rule);
        }

        var r = @base is JsonObject bo ? (JsonObject)bo.DeepClone() : [];
        r["id"] = id ?? Str(@base, "id") ?? "bw-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        r["name"] = name.Trim().Length == 0 ? "Rule" : name.Trim();
        r["matchType"] = any ? "any" : "all";
        r["conditions"] = cj;
        r["actions"] = aj;
        r["stopProcessing"] = stop;
        if (!r.ContainsKey("enabled"))
        {
            r["enabled"] = true;
        }

        SetOrRemove(r, "includeSpam", includeSpam ? JsonValue.Create(true) : null);
        var f = SieveScript.DayBoundary(from ?? string.Empty, false);
        var u = SieveScript.DayBoundary(until ?? string.Empty, true);
        if (PeriodProblem(f, u) is { } problem)
        {
            return Bad(problem);
        }

        SetOrRemove(r, "activeFrom", f.Value);
        SetOrRemove(r, "activeUntil", u.Value);
        return new FilterBuilt(r, null);
    }

    private static string? PeriodProblem(Barbican.Core.Common.Result<string?> from, Barbican.Core.Common.Result<string?> until)
    {
        if (!from.IsOk)
        {
            return from.Error!.Message;
        }

        if (!until.IsOk)
        {
            return until.Error!.Message;
        }

        return from.Value is not null && until.Value is not null && string.CompareOrdinal(until.Value, from.Value) <= 0 ? "The period ends before it starts." : null;
    }

    /// <summary>"From contains x or Has an attachment → Star message, Move to folder Archive · then stop" (T-095).</summary>
    public static string Summary(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var join = string.Equals(Str(rule, "matchType"), "any", StringComparison.Ordinal) ? " or " : " and ";
        var conds = ConditionsOf(rule).Select(c =>
        {
            var label = Fields.FirstOrDefault(x => string.Equals(x.Id, c.Field, StringComparison.Ordinal)).Label ?? "If";
            return c.Field switch
            {
                "all" => "Every message",
                "header" => $"{c.Header} {ComparatorLabel(c.Comparator)} {c.Value}",
                "attachment" when string.Equals(c.Comparator, "has_any", StringComparison.Ordinal) => "Has an attachment",
                _ => $"{label} {ComparatorLabel(c.Comparator)} {c.Value}",
            };
        });
        var actions = ActionsOf(rule);
        var acts = actions.Select(a =>
        {
            var label = Actions.FirstOrDefault(x => string.Equals(x.Id, a.Kind, StringComparison.Ordinal)).Label ?? "act";
            return a.Value.Length == 0 ? label : $"{label} {a.Value}";
        });
        var s = $"{string.Join(join, conds)} → {string.Join(", ", acts)}";
        if (rule["stopProcessing"] is JsonValue sv && sv.TryGetValue<bool>(out var st) && st && !actions.Any(a => string.Equals(a.Kind, "stop", StringComparison.Ordinal)))
        {
            s += " · then stop";
        }

        if (Str(rule, "activeUntil") is { } until)
        {
            s += " · until " + until[..Math.Min(10, until.Length)];
        }

        return s;
    }

    /// <summary>The rules with <paramref name="id"/> moved one place up (-1) or down (1); unchanged at the ends or for an unknown id (T-095).</summary>
    public static IReadOnlyList<JsonNode> Moved(IReadOnlyList<JsonNode> rules, string id, int direction)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var v = rules.ToList();
        var i = v.FindIndex(r => string.Equals(Str(r, "id"), id, StringComparison.Ordinal));
        var j = i + direction;
        if (i >= 0 && j >= 0 && j < v.Count)
        {
            (v[i], v[j]) = (v[j], v[i]);
        }

        return v;
    }

    internal static string? Str(JsonNode? n, string key) => n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    // Rust filters_x.rs:135 valid_email
    private static bool ValidEmail(string e)
    {
        var at = e.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var local = e[..at];
        var domain = e[(at + 1)..];
        return local.Length > 0 && domain.Contains('.', StringComparison.Ordinal) && !e.Any(char.IsWhiteSpace) && !domain.StartsWith('.') && !domain.EndsWith('.');
    }

    // Rust filters_x.rs:142 header_ok, Bulwark generator.ts:29 isValidSieveHeaderName: the names the generator writes (it skips rules with any other name)
    private static bool HeaderOk(string h) => h.Length > 0 && h.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-');

    private static FilterBuilt BuildCondition(FilterCond c)
    {
        var o = new JsonObject { ["field"] = c.Field };
        switch (c.Field)
        {
            case "all":
                o["comparator"] = "any";
                o["value"] = string.Empty;
                break;
            case "attachment" when string.Equals(c.Comparator, "has_any", StringComparison.Ordinal):
                o["comparator"] = "has_any";
                o["value"] = string.Empty;
                break;
            case "size":
                var v = c.Value.Trim().ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
                var digits = v.TrimEnd('K', 'M', 'G');
                if (digits.Length == 0 || !digits.All(char.IsAsciiDigit) || v.Length - digits.Length > 1)
                {
                    return Bad($"\"{c.Value.Trim()}\" is not a size (use e.g. 500K or 10M)");
                }

                o["comparator"] = string.Equals(c.Comparator, "less_than", StringComparison.Ordinal) ? "less_than" : "greater_than";
                o["value"] = v;
                break;
            default:
                return BuildTextCondition(c, o);
        }

        return new FilterBuilt(o, null);
    }

    private static FilterBuilt BuildTextCondition(FilterCond c, JsonObject o)
    {
        if (string.Equals(c.Field, "header", StringComparison.Ordinal))
        {
            if (!HeaderOk(c.Header.Trim()))
            {
                return Bad("Enter a header name (letters, digits and dashes)");
            }

            o["headerName"] = c.Header.Trim();
        }

        var allowed = Comparators(c.Field);
        var cmp = allowed.Contains(c.Comparator, StringComparer.Ordinal) ? c.Comparator : allowed.Count > 0 ? allowed[0] : "contains";
        var isRegex = string.Equals(cmp, "regex", StringComparison.Ordinal);

        // a regular expression is one value: commas in it are not separators
        var vals = isRegex ? [c.Value.Trim()] : c.Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (isRegex && vals[0].Length > 0)
        {
            try
            {
                _ = new Regex(vals[0], RegexOptions.None, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException e)
            {
                return Bad($"\"{vals[0]}\" is not a valid regular expression: {e.Message.Split('\n')[^1].Trim()}");
            }
        }

        if (vals.All(x => x.Length == 0))
        {
            return Bad($"Add a value for \"{Fields.FirstOrDefault(f => string.Equals(f.Id, c.Field, StringComparison.Ordinal)).Label ?? "the condition"}\"");
        }

        o["comparator"] = cmp;
        o["value"] = vals.Count == 1 ? vals[0] : new JsonArray([.. vals.Select(x => (JsonNode?)JsonValue.Create(x))]);
        return new FilterBuilt(o, null);
    }

    private static FilterBuilt BuildAction(FilterAct a, IReadOnlyList<FilterFolder> folders)
    {
        var needs = Actions.FirstOrDefault(x => string.Equals(x.Id, a.Kind, StringComparison.Ordinal)).Needs ?? string.Empty;
        var o = new JsonObject { ["type"] = a.Kind };
        switch (needs)
        {
            case "folder":
                var folder = folders.FirstOrDefault(f => string.Equals(f.Id, a.Mailbox, StringComparison.Ordinal));
                if (folder is null)
                {
                    return Bad("Select a folder for this action");
                }

                o["value"] = folder.Path;
                o["mailboxId"] = folder.Id;
                break;
            case "tag":
                if (a.Value.Trim().Length == 0)
                {
                    return Bad("Select a tag for this action");
                }

                o["value"] = a.Value.Trim();
                break;
            case "address":
                if (!ValidEmail(a.Value.Trim()))
                {
                    return Bad("Enter an address to forward to");
                }

                o["value"] = a.Value.Trim();
                if (a.KeepCopy)
                {
                    o["keepCopy"] = true;
                }

                break;
            case "text":
                if (a.Value.Trim().Length == 0)
                {
                    return Bad("Enter the rejection message");
                }

                o["value"] = a.Value.Trim();
                break;
            default:
                break;
        }

        return new FilterBuilt(o, null);
    }

    private static void SetOrRemove(JsonObject o, string key, JsonNode? value)
    {
        if (value is null)
        {
            o.Remove(key);
        }
        else
        {
            o[key] = value;
        }
    }

    private static FilterBuilt Bad(string why) => new(null, why);

    private static string ValuesText(JsonNode? v) => v switch
    {
        JsonArray a => string.Join(", ", a.Select(x => x is JsonValue s && s.TryGetValue<string>(out var t) ? t : null).OfType<string>()),
        JsonValue s when s.TryGetValue<string>(out var t) => t,
        JsonValue n when n.GetValueKind() == System.Text.Json.JsonValueKind.Number => n.ToJsonString(),
        _ => string.Empty,
    };
}
