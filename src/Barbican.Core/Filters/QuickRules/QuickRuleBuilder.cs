// Mirrors: Rust quick_rules.rs:44 extract_list_id, :55 subject_of, :75 sieve_path, :105 preset_rule, :180 apply, :218 plan; Bulwark lib/quick-rule.ts
// (applyQuickRule, the preset rules), RFC 2919 (List-Id); Stalwart SieveScript set (crates/jmap/src/sieve/set.rs).

using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;
using Barbican.Core.Sieve;

namespace Barbican.Core.Filters.QuickRules;

/// <summary>The one-click rules of the Rules menu, as pure functions over the parsed script.</summary>
public static class QuickRuleBuilder
{
    /// <summary>The message shown when the script was edited by hand and so is never rewritten.</summary>
    public const string HandEditedMessage = "Your filters were edited by hand. Change them in Settings > Filters.";

    private static string? S(JsonNode? n, string key) => n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Normalize(string a) => a.Trim().ToLowerInvariant();

    private static string? AddressDomain(string a)
    {
        var at = a.LastIndexOf('@');
        return at > 0 && at < a.Length - 1 ? a[(at + 1)..].ToLowerInvariant() : null;
    }

    /// <summary>A fresh rule id in Bulwark's shape.</summary>
    public static string NewId() => "bw-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

    /// <summary>The list id in a List-Id header value (the part in angle brackets, or a bare token); null when it has spaces.</summary>
    public static string? ExtractListId(string raw)
    {
        var v = raw.Trim();
        var i = v.LastIndexOf('<');
        var id = (i >= 0 && v.EndsWith('>') ? v[(i + 1)..^1] : v).Trim();
        return id.Length > 0 && !id.Any(char.IsWhiteSpace) ? id : null;
    }

    /// <summary>The sender(s), domain and list of a message; the user's own addresses are left out.</summary>
    public static QuickRuleSubject SubjectOf(JsonNode message, IReadOnlyList<string> ownAddresses)
    {
        var senders = new List<string>();
        if (message["from"] is JsonArray from)
        {
            foreach (var f in from)
            {
                var a = Normalize(S(f, "email") ?? string.Empty);
                if (a.Length == 0 || !a.Contains('@', StringComparison.Ordinal) || ownAddresses.Any(o => string.Equals(Normalize(o), a, StringComparison.Ordinal)) || senders.Contains(a, StringComparer.Ordinal))
                {
                    continue;
                }

                senders.Add(a);
            }
        }

        string? domain = null;
        foreach (var s in senders)
        {
            var d = AddressDomain(s);
            if (d is null || (domain is not null && !string.Equals(domain, d, StringComparison.Ordinal)))
            {
                domain = null;
                break;
            }

            domain = d;
        }

        string? listId = null;
        if (message["headers"] is JsonArray headers)
        {
            var h = headers.FirstOrDefault(x => string.Equals(S(x, "name"), "List-Id", StringComparison.OrdinalIgnoreCase));
            if (h is not null && S(h, "value") is { } value)
            {
                listId = ExtractListId(value.Replace('\r', ' ').Replace('\n', ' '));
            }
        }

        return new QuickRuleSubject(senders, domain, listId);
    }

    /// <summary>The Sieve fileinto path of a mailbox: Inbox children are INBOX/name, role folders their own name.</summary>
    public static string SievePath(IReadOnlyList<SieveFolderRef> folders, string id)
    {
        var segs = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cur = folders.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
        while (cur is not null)
        {
            if (!seen.Add(cur.Id))
            {
                break;
            }

            segs.Insert(0, cur.Role is "inbox" ? "INBOX" : cur.Name);
            var parent = cur.ParentId;
            cur = parent is null ? null : folders.FirstOrDefault(x => string.Equals(x.Id, parent, StringComparison.Ordinal));
        }

        return string.Join('/', segs);
    }

    /// <summary>How rule names refer to the senders.</summary>
    public static string SenderName(QuickRuleSubject s) => s.Senders.Count == 1 ? s.Senders[0] : $"{s.Senders.Count} senders";

    private static JsonNode SenderValue(QuickRuleSubject s) =>
        s.Senders.Count == 1 ? JsonValue.Create(s.Senders[0])! : new JsonArray(s.Senders.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());

    private static JsonObject MoveAction(QuickRuleFolder f) => new JsonObject { ["type"] = "move", ["value"] = f.Path, ["mailboxId"] = f.Id };

    /// <summary>The Bulwark rule object a preset makes.</summary>
    public static JsonNode PresetRule(QuickRulePreset preset, QuickRuleSubject subject, string id)
    {
        var sender = SenderName(subject);
        JsonNode From() => new JsonObject { ["field"] = "from", ["comparator"] = "address_is", ["value"] = SenderValue(subject) };
        string name;
        JsonNode cond;
        JsonNode action;
        var includeSpam = false;
        switch (preset)
        {
            case QuickRulePreset.MoveSender p:
                (name, cond, action) = ($"Move from {sender} to {p.Folder.Name}", From(), MoveAction(p.Folder));
                break;
            case QuickRulePreset.MoveDomain p:
                var d = subject.Domain ?? string.Empty;
                (name, cond, action) = ($"Move from @{d} to {p.Folder.Name}", new JsonObject { ["field"] = "from", ["comparator"] = "domain_is", ["value"] = d }, MoveAction(p.Folder));
                break;
            case QuickRulePreset.MoveList p:
                var l = subject.ListId ?? string.Empty;
                (name, cond, action) = ($"Move list {l} to {p.Folder.Name}", new JsonObject { ["field"] = "header", ["headerName"] = "List-Id", ["comparator"] = "contains", ["value"] = l }, MoveAction(p.Folder));
                break;
            case QuickRulePreset.MarkRead:
                (name, cond, action) = ($"Mark as read from {sender}", From(), new JsonObject { ["type"] = "mark_read" });
                break;
            case QuickRulePreset.Tag t:
                (name, cond, action) = ($"Tag as {t.Name} from {sender}", From(), new JsonObject { ["type"] = "add_label", ["value"] = t.Id });
                break;
            case QuickRulePreset.Block b:
                // the spam guard would skip exactly the mail this rule is for
                (name, cond, action) = ($"Block {sender}", From(), MoveAction(b.Folder));
                includeSpam = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(preset));
        }

        var r = new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["enabled"] = true,
            ["matchType"] = "all",
            ["conditions"] = new JsonArray(cond),
            ["actions"] = new JsonArray(action),
            ["stopProcessing"] = true,
        };
        if (includeSpam)
        {
            r["includeSpam"] = true;
        }

        return r;
    }

    private static List<string> Values(JsonNode? v)
    {
        if (v is JsonArray a)
        {
            return a.Select(x => x is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
        }

        return v is JsonValue j && j.TryGetValue<string>(out var str) && str.Length > 0 ? [str] : [];
    }

    private static bool SameConditionKind(JsonNode a, JsonNode b) =>
        string.Equals(S(a, "field"), S(b, "field"), StringComparison.Ordinal) && string.Equals(S(a, "comparator"), S(b, "comparator"), StringComparison.Ordinal)
        && string.Equals(S(a, "headerName") ?? string.Empty, S(b, "headerName") ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool SameAction(JsonNode a, JsonNode b)
    {
        var t = S(a, "type");
        if (!string.Equals(t, S(b, "type"), StringComparison.Ordinal))
        {
            return false;
        }

        switch (t)
        {
            case "move" or "copy":
                var x = S(a, "mailboxId");
                var y = S(b, "mailboxId");
                return !string.IsNullOrEmpty(x) && !string.IsNullOrEmpty(y) ? string.Equals(x, y, StringComparison.Ordinal) : string.Equals(S(a, "value") ?? string.Empty, S(b, "value") ?? string.Empty, StringComparison.Ordinal);
            case "add_label" or "forward" or "reject":
                return string.Equals(S(a, "value") ?? string.Empty, S(b, "value") ?? string.Empty, StringComparison.Ordinal);
            default:
                return true;
        }
    }

    private static bool SameActions(List<JsonNode> a, List<JsonNode> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        var left = b.ToList();
        foreach (var x in a)
        {
            var i = left.FindIndex(y => SameAction(x, y));
            if (i < 0)
            {
                return false;
            }

            left.RemoveAt(i);
        }

        return true;
    }

    private static bool HasPeriod(JsonNode r) => !string.IsNullOrEmpty(S(r, "activeFrom")) || !string.IsNullOrEmpty(S(r, "activeUntil"));

    private static List<JsonNode> Items(JsonNode? n) => n is JsonArray a ? a.Where(x => x is not null).Select(x => x!).ToList() : [];

    /// <summary>Creates the rule at the top, merges its values into an equivalent enabled rule without a period, or reports it as covered.</summary>
    public static QuickRuleOutcome Apply(IReadOnlyList<JsonNode> rules, JsonNode candidate)
    {
        var conds = Items(candidate["conditions"]);
        var acts = Items(candidate["actions"]);
        var target = -1;
        if (conds.Count == 1)
        {
            for (var i = 0; i < rules.Count; i++)
            {
                var r = rules[i];
                var origin = S(r, "origin");
                var rc = Items(r["conditions"]);
                var enabled = !(r["enabled"] is JsonValue ev && ev.TryGetValue<bool>(out var eb) && !eb);
                if ((origin is null || origin is "bulwark") && enabled && !HasPeriod(r) && rc.Count == 1 && SameConditionKind(rc[0], conds[0]) && SameActions(Items(r["actions"]), acts))
                {
                    target = i;
                    break;
                }
            }
        }

        if (target < 0)
        {
            var created = new List<JsonNode> { candidate.DeepClone() };
            created.AddRange(rules.Select(r => r.DeepClone()));
            return new QuickRuleOutcome.Created(created);
        }

        var t = rules[target];
        var name = S(t, "name") ?? string.Empty;
        var existing = Values(t["conditions"]?[0]?["value"]);
        var known = existing.Select(v => v.ToLowerInvariant()).ToList();
        var added = 0;
        foreach (var v in Values(conds[0]["value"]))
        {
            if (known.Contains(v.ToLowerInvariant()))
            {
                continue;
            }

            known.Add(v.ToLowerInvariant());
            existing.Add(v);
            added++;
        }

        if (added == 0)
        {
            return new QuickRuleOutcome.Covered(name);
        }

        var merged = t.DeepClone();
        merged["conditions"]![0]!["value"] = new JsonArray(existing.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        var all = rules.Select(r => r.DeepClone()).ToList();
        all[target] = merged;
        return new QuickRuleOutcome.Merged(all, name);
    }

    /// <summary>What saving the candidate into the parsed script does; a hand-edited (locked) script is refused with "edited by hand".</summary>
    public static Result<QuickRulePlan> Plan(SieveParsed script, JsonNode candidate)
    {
        if (script.Locked)
        {
            return Result<QuickRulePlan>.Fail(new ValidationError(HandEditedMessage));
        }

        var rules = Items(script.Metadata["rules"]);
        return Result<QuickRulePlan>.Ok(Apply(rules, candidate) switch
        {
            QuickRuleOutcome.Covered c => new QuickRulePlan(null, $"Already covered by \"{c.Name}\""),
            QuickRuleOutcome.Created c => new QuickRulePlan(c.Rules, $"Rule \"{S(candidate, "name") ?? string.Empty}\" created"),
            QuickRuleOutcome.Merged m => new QuickRulePlan(m.Rules, $"Added to rule \"{m.Name}\""),
            _ => throw new InvalidOperationException(),
        });
    }
}
