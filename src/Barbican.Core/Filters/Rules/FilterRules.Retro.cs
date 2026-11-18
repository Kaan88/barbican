// Mirrors: Rust filters_x.rs :231 TEXT_CMPS, :232 ADDRESS_HEADERS, :233 RAW_ONLY, :249 retro_unsupported, :273 sieve_matches, :306 text_matches, :314 cond_matches, :329 rule_matches, :340 query_filter, :361 retro_properties, :378 retro_message, :404 plan, :428 plan_count, :437 step_patch;
// Bulwark lib/filters/retroactive.ts (:82 retroactiveSupport, :108 sieveMatches, :188 ruleMatches, :204 retroQueryFilter, :225 retroProperties, :248 toRetroMessage, :309 planRetroactive); RFC 5228 section 2.7.1 (:matches), RFC 8621 section 4.4.1 (Email/query filters). T-096, T-097, T-099, T-101.

using System.Text.Json.Nodes;

namespace Barbican.Core.Filters.Rules;

public static partial class FilterRules
{
    private static readonly string[] TextCmps = ["contains", "not_contains", "is", "not_is", "starts_with", "ends_with", "matches", "address_is", "domain_is"];

    private static readonly string[] AddressHeaders = ["from", "sender", "reply-to", "to", "cc", "bcc", "resent-from", "resent-sender", "resent-reply-to", "resent-to", "resent-cc", "resent-bcc"];

    private static readonly string[] RawOnly =
    [
        "date", "resent-date", "message-id", "resent-message-id", "in-reply-to", "references", "return-path", "received",
        "list-help", "list-unsubscribe", "list-subscribe", "list-post", "list-owner", "list-archive", "list-id-raw", "x-none",
    ];

    /// <summary>Why the rule cannot run on existing mail (a body, size, regex or header-only condition; forward, discard or reject; an active period; a move without a mailbox id), or null when it can (T-097).</summary>
    public static string? RetroUnsupported(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule["activeFrom"] is not null || rule["activeUntil"] is not null)
        {
            return "This rule only acts on mail arriving in its active period, so it says nothing about mail you already have.";
        }

        var conds = rule["conditions"] as JsonArray ?? [];
        if (conds.Count == 0)
        {
            return "This rule has no conditions.";
        }

        foreach (var c in conds)
        {
            if (Str(c, "comparator") is not { } cmp || !TextCmps.Contains(cmp, StringComparer.Ordinal) || CondHeader(c) is null)
            {
                return "Body, size, attachment and regex conditions can only be checked by the server as mail arrives.";
            }
        }

        var acts = rule["actions"] as JsonArray ?? [];
        static bool Applied(string t) => t is "move" or "copy" or "mark_read" or "star" or "add_label";
        if (!acts.Any(a => Str(a, "type") is { } t && Applied(t)))
        {
            return "None of this rule's actions changes a message that is already delivered.";
        }

        foreach (var a in acts)
        {
            var t = Str(a, "type") ?? string.Empty;
            if (!Applied(t) && !string.Equals(t, "stop", StringComparison.Ordinal))
            {
                return "Forward, reject, discard and keep never run on mail you already have.";
            }

            if (t is "move" or "copy" && Str(a, "mailboxId") is null)
            {
                return "Pick the folder again in the rule (it is only known by its name).";
            }

            if (string.Equals(t, "add_label", StringComparison.Ordinal) && string.IsNullOrEmpty(Str(a, "value")))
            {
                return "The tag action has no tag.";
            }
        }

        return null;
    }

    /// <summary>Sieve :matches: "*" any run, "?" one character, backslash escapes (RFC 5228 section 2.7.1); the case folding is the caller's (T-096).</summary>
    public static bool SieveMatches(string pattern, string text)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(text);

        // tokens: a literal character, "any one" (?) or "star" (*)
        var toks = new List<(char C, int Kind)>();
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\\' && i + 1 < pattern.Length)
            {
                toks.Add((pattern[i + 1], 0));
                i++;
            }
            else if (pattern[i] == '*')
            {
                toks.Add(('*', 2));
            }
            else if (pattern[i] == '?')
            {
                toks.Add(('?', 1));
            }
            else
            {
                toks.Add((pattern[i], 0));
            }
        }

        int n = toks.Count, m = text.Length;
        var dp = new bool[n + 1, m + 1];
        dp[0, 0] = true;
        for (var a = 1; a <= n; a++)
        {
            if (toks[a - 1].Kind == 2)
            {
                dp[a, 0] = dp[a - 1, 0];
            }

            for (var b = 1; b <= m; b++)
            {
                dp[a, b] = toks[a - 1].Kind switch
                {
                    2 => dp[a - 1, b] || dp[a, b - 1],
                    1 => dp[a - 1, b - 1],
                    _ => dp[a - 1, b - 1] && toks[a - 1].C == text[b - 1],
                };
            }
        }

        return dp[n, m];
    }

    /// <summary>The Email/query filter that narrows the scan: the mailbox plus the address conditions as JMAP filters (T-097).</summary>
    public static JsonNode QueryFilter(JsonNode rule, string mailboxId)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(mailboxId);
        var conds = rule["conditions"] as JsonArray ?? [];
        JsonNode? Narrow(JsonNode? c)
        {
            if (!AddressTest(c))
            {
                return null;
            }

            var vs = Values(c?["value"]);
            if (vs.Count == 0)
            {
                return null;
            }

            var field = Str(c, "field") ?? "from";
            var each = vs.Select(v => (JsonNode)new JsonObject { [field] = v }).ToList();
            return each.Count == 1 ? each[0] : new JsonObject { ["operator"] = "OR", ["conditions"] = new JsonArray([.. each]) };
        }

        var narrowed = conds.Select(Narrow).ToList();
        JsonNode Inbox() => new JsonObject { ["inMailbox"] = mailboxId };
        if (string.Equals(Str(rule, "matchType"), "any", StringComparison.Ordinal))
        {
            if (narrowed.Count == 0 || narrowed.Any(x => x is null))
            {
                return Inbox();
            }

            var parts = narrowed.OfType<JsonNode>().ToList();
            return new JsonObject
            {
                ["operator"] = "AND",
                ["conditions"] = new JsonArray(Inbox(), parts.Count == 1 ? parts[0] : new JsonObject { ["operator"] = "OR", ["conditions"] = new JsonArray([.. parts]) }),
            };
        }

        var all = narrowed.OfType<JsonNode>().ToList();
        return all.Count == 0 ? Inbox() : new JsonObject { ["operator"] = "AND", ["conditions"] = new JsonArray([Inbox(), .. all]) };
    }

    /// <summary>The Email/get properties the rule's conditions need (T-097, T-101).</summary>
    public static IReadOnlyList<string> RetroProperties(JsonNode rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        var p = new List<string> { "mailboxIds", "keywords" };
        foreach (var c in rule["conditions"] as JsonArray ?? [])
        {
            if (AddressTest(c))
            {
                p.Add(Str(c, "field") ?? "from");
                continue;
            }

            if (CondHeader(c) is not { } h)
            {
                continue;
            }

            var l = h.ToLowerInvariant();
            if (AddressHeaders.Contains(l, StringComparer.Ordinal))
            {
                p.Add($"header:{h}:asAddresses:all");
            }
            else if (RawOnly.Contains(l, StringComparer.Ordinal))
            {
                p.Add($"header:{h}:all");
            }
            else
            {
                p.Add($"header:{h}:asText:all");
                p.Add($"header:{h}:all");
            }
        }

        return [.. p.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>An Email/get record as the matcher reads it: id, lower-cased addresses, unfolded headers by lower-case name, keywords, mailboxIds (T-097, T-101).</summary>
    public static JsonNode RetroMessage(JsonNode record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var headers = new JsonObject();
        if (record is JsonObject o)
        {
            foreach (var (k, v) in o)
            {
                if (!k.StartsWith("header:", StringComparison.Ordinal))
                {
                    continue;
                }

                var rest = k["header:".Length..];
                if (rest.EndsWith(":asAddresses:all", StringComparison.Ordinal))
                {
                    var h = rest[..^":asAddresses:all".Length];
                    headers[h.ToLowerInvariant()] = new JsonArray([.. List(v).Select(line => (JsonNode?)JsonValue.Create(string.Join(", ", List(line).Select(AddressText).OfType<string>())))]);
                }
                else if (rest.EndsWith(":asText:all", StringComparison.Ordinal))
                {
                    var h = rest[..^":asText:all".Length];
                    var raw = List(o[$"header:{h}:all"]);
                    var occ = List(v).Select((t, i) => Text(t) ?? (i < raw.Count ? Unfold(Text(raw[i])) : null)).OfType<string>();
                    headers[h.ToLowerInvariant()] = new JsonArray([.. occ.Select(x => (JsonNode?)JsonValue.Create(x))]);
                }
                else if (rest.EndsWith(":all", StringComparison.Ordinal))
                {
                    var h = rest[..^":all".Length];
                    if (!o.ContainsKey($"header:{h}:asText:all"))
                    {
                        headers[h.ToLowerInvariant()] = new JsonArray([.. List(v).Select(x => Text(x)).OfType<string>().Select(x => (JsonNode?)JsonValue.Create(Unfold(x)))]);
                    }
                }
            }
        }

        return new JsonObject
        {
            ["id"] = record["id"]?.DeepClone(),
            ["mailboxIds"] = record["mailboxIds"]?.DeepClone(),
            ["keywords"] = record["keywords"]?.DeepClone(),
            ["from"] = record["from"]?.DeepClone(),
            ["to"] = record["to"]?.DeepClone(),
            ["cc"] = record["cc"]?.DeepClone(),
            ["headers"] = headers,
        };
    }

    /// <summary>Whether the rule matches a message as <see cref="RetroMessage"/> built it: the conditions joined by the rule's match type, and the spam guard (a rule that files mail skips junk unless it opts in).</summary>
    public static bool RuleMatches(JsonNode rule, JsonNode message)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(message);
        var conds = rule["conditions"] as JsonArray ?? [];
        if (conds.Count == 0)
        {
            return false;
        }

        var res = conds.Select(c => CondMatches(c, message)).ToList();
        var ok = string.Equals(Str(rule, "matchType"), "any", StringComparison.Ordinal) ? res.Any(x => x) : res.All(x => x);
        var spamOk = rule["includeSpam"] is JsonValue sp && sp.TryGetValue<bool>(out var inc) && inc;
        var files = (rule["actions"] as JsonArray ?? []).Any(a => Str(a, "type") is "move" or "copy");
        var junk = message["keywords"]?["$junk"] is JsonValue j && j.TryGetValue<bool>(out var isJunk) && isJunk;
        return ok && !(!spamOk && files && junk);
    }

    /// <summary>The steps that apply the rule to the matching messages: keyword steps for mark_read, star and add_label (skipping messages that already have it), a move step (the first move) and copy steps, each only for messages not already in the folder (T-097).</summary>
    public static IReadOnlyList<FilterPlanStep> Plan(JsonNode rule, IReadOnlyList<JsonNode> messages)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(messages);
        if (RetroUnsupported(rule) is not null)
        {
            return [];
        }

        var matched = messages.Where(m => RuleMatches(rule, m)).ToList();
        var steps = new List<FilterPlanStep>();
        var acts = (rule["actions"] as JsonArray ?? []).ToList();
        foreach (var a in acts.Where(a => Str(a, "type") is "mark_read" or "star" or "add_label"))
        {
            var kw = Str(a, "type") switch
            {
                "mark_read" => "$seen",
                "star" => "$flagged",
                _ => "$label:" + (Str(a, "value") ?? string.Empty),
            };
            var ids = matched.Where(m => !Flag(m["keywords"], kw)).Select(m => Str(m, "id") ?? string.Empty).ToList();
            if (ids.Count > 0)
            {
                steps.Add(new FilterPlanStep("keyword", ids, kw));
            }
        }

        var files = acts.Where(a => Str(a, "type") is "move" or "copy" && Str(a, "mailboxId") is not null).ToList();
        var firstMove = files.FindIndex(a => string.Equals(Str(a, "type"), "move", StringComparison.Ordinal));
        List<string> NotIn(string mailbox) => [.. matched.Where(m => !Flag(m["mailboxIds"], mailbox)).Select(m => Str(m, "id") ?? string.Empty)];
        for (var i = 0; i < files.Count; i++)
        {
            var mb = Str(files[i], "mailboxId")!;
            var ids = NotIn(mb);
            if (ids.Count > 0)
            {
                steps.Add(new FilterPlanStep(i == firstMove ? "move" : "copy", ids, mb));
            }
        }

        // the move comes before the copies, as Sieve files into the first folder and then copies
        return [.. steps.Where(s => string.Equals(s.Kind, "keyword", StringComparison.Ordinal)), .. steps.Where(s => string.Equals(s.Kind, "move", StringComparison.Ordinal)), .. steps.Where(s => string.Equals(s.Kind, "copy", StringComparison.Ordinal))];
    }

    /// <summary>How many distinct messages the plan touches (T-097).</summary>
    public static int PlanCount(IReadOnlyList<FilterPlanStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return steps.SelectMany(s => s.Ids).Distinct(StringComparer.Ordinal).Count();
    }

    /// <summary>The Email/set patch of one step with "~" and "/" escaped (<c>mailboxIds/a~1b~0c</c>); a move replaces the folders (T-099).</summary>
    public static JsonObject StepPatch(string kind, string arg)
    {
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(arg);
        return kind switch
        {
            "keyword" => new JsonObject { ["keywords/" + Pointer(arg)] = true },
            "move" => new JsonObject { ["mailboxIds"] = new JsonObject { [arg] = true } },
            _ => new JsonObject { ["mailboxIds/" + Pointer(arg)] = true },
        };
    }

    private static string Pointer(string k) => k.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);

    private static bool Flag(JsonNode? map, string key) => map is JsonObject o && o[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static string? CondHeader(JsonNode? c) => Str(c, "field") switch
    {
        "header" => Str(c, "headerName")?.Trim() is { } h && HeaderOk(h) ? h : null,
        "from" => "From",
        "to" => "To",
        "cc" => "Cc",
        "subject" => "Subject",
        _ => null,
    };

    private static bool AddressTest(JsonNode? c) => Str(c, "comparator") is "address_is" or "domain_is" && Str(c, "field") is "from" or "to" or "cc";

    // ASCII-only folding, as Sieve's i;ascii-casemap
    private static string AsciiLower(string s) => string.Create(s.Length, s, static (span, src) =>
    {
        for (var i = 0; i < span.Length; i++)
        {
            var c = src[i];
            span[i] = c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;
        }
    });

    private static bool TextMatches(string cmp, string key, string subject)
    {
        var (k, s) = (AsciiLower(key), AsciiLower(subject));
        return cmp switch
        {
            "is" or "not_is" => string.Equals(s, k, StringComparison.Ordinal),
            "starts_with" => SieveMatches(k + "*", s),
            "ends_with" => SieveMatches("*" + k, s),
            "matches" => SieveMatches(k, s),
            _ => s.Contains(k, StringComparison.Ordinal),
        };
    }

    private static List<string> Values(JsonNode? v) => v switch
    {
        JsonArray a => [.. a.Select(x => Text(x)).OfType<string>().Where(x => x.Length > 0)],
        JsonValue s when s.TryGetValue<string>(out var t) && t.Length > 0 => [t],
        _ => [],
    };

    private static bool CondMatches(JsonNode? c, JsonNode m)
    {
        var vs = Values(c?["value"]);
        if (vs.Count == 0)
        {
            return false;
        }

        if (AddressTest(c))
        {
            var addrs = (m[Str(c, "field") ?? "from"] as JsonArray ?? []).Select(x => Str(x, "email")?.Trim()).OfType<string>().Where(e => e.Length > 0).ToList();
            var parts = string.Equals(Str(c, "comparator"), "domain_is", StringComparison.Ordinal) ? addrs.Select(a => a.Contains('@', StringComparison.Ordinal) ? a[(a.LastIndexOf('@') + 1)..] : string.Empty).ToList() : addrs;
            return parts.Any(p => vs.Any(v => string.Equals(AsciiLower(v), AsciiLower(p), StringComparison.Ordinal)));
        }

        var h = (CondHeader(c) ?? string.Empty).ToLowerInvariant();
        var occ = (m["headers"]?[h] as JsonArray ?? []).Select(x => Text(x)).OfType<string>().ToList();
        var cmp = Str(c, "comparator") is { } raw && raw is not ("address_is" or "domain_is") ? raw : "contains";
        var pos = occ.Any(o => vs.Any(v => TextMatches(cmp, v, o)));
        return cmp is "not_contains" or "not_is" ? !pos : pos;
    }

    private static List<JsonNode?> List(JsonNode? v) => v switch
    {
        JsonArray a => [.. a],
        null => [],
        _ => [v],
    };

    private static string? Text(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string Unfold(string? v) => (v ?? string.Empty).Replace("\r\n", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal).Trim();

    private static string? AddressText(JsonNode? a)
    {
        var (n, e) = (Str(a, "name") ?? string.Empty, Str(a, "email") ?? string.Empty);
        return e.Length == 0 ? (n.Length > 0 ? n : null) : n.Length == 0 ? e : $"{n} <{e}>";
    }
}
