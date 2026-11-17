// Mirrors: Rust sieve.rs:10 EXTERNAL, :25 parse, :175 scan_top_level, :239 vacation_only, :399 period_boundary, :432 period_tests, :478 generate, :483 with_rules,
// :492 generate_meta, :613 rule, :626 day_boundary; Bulwark lib/sieve/{parser,generator,period}.ts; RFC 5228 (Sieve), RFC 5229 (variables is not used),
// RFC 5230 (vacation), RFC 5260 (date), RFC 5231 (relational), RFC 5435 (spamtest); Stalwart crates/jmap/src/sieve/{get,set,validate}.rs.
// M6 rows (T-271..T-279) ported early because quick rules call the parser and the generator (M2 plan 2.1). Implemented by stream search (M2).

#pragma warning disable MA0051 // ported parsers and generators read top to bottom in the order of the Rust source

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Barbican.Core.Common;
using Barbican.Core.Jmap.Errors;

namespace Barbican.Core.Sieve;

/// <summary>The Sieve parser and generator over Bulwark's metadata block.</summary>
public static class SieveScript
{
    /// <summary>The marker line above rules that are not Bulwark's.</summary>
    public const string External = "# --- External rules (managed outside Bulwark) ---";

    private const string Begin = "/* @metadata:begin";
    private const string End = "@metadata:end */";
    private const string VacationForwardMarker = "# Vacation forwarding";
    private const string SpamGuard = "not spamtest :percent :value \"ge\" :comparator \"i;ascii-numeric\" \"50\"";
    private const string Now = "currentdate :zone \"+0000\"";

    private static readonly string[] ControlWords = ["if", "elsif", "else"];
    private static readonly string[] KnownKeys = ["version", "rules", "vacation", "includeVacation", "vacationForward", "vacationAudience"];
    private static readonly string[] SpamGuardRequires = ["spamtestplus", "relational", "comparator-i;ascii-numeric"];
    private static readonly string[] PeriodRequires = ["date", "relational"];

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };

    // ---------------- small JSON helpers ----------------

    private static string? S(JsonNode? n, string key) => n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static bool Is(JsonNode? n, string key, bool b) => n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<bool>(out var x) && x == b;

    private static bool IsStr(JsonNode? n, string key, string value) => string.Equals(S(n, key), value, StringComparison.Ordinal);

    private static bool Has(JsonNode? n, string key) => n is JsonObject o && o.ContainsKey(key);

    private static JsonArray Arr(JsonNode? n, string key) => n is JsonObject o && o[key] is JsonArray a ? a : [];

    private static string Json(JsonNode n) => n.ToJsonString(Compact);

    // ---------------- parse ----------------

    /// <summary>Splits a script into the metadata, the external part and the locked flag; a vacation-only script becomes the auto-reply.</summary>
    public static SieveParsed Parse(string script)
    {
        var b = script.IndexOf(Begin, StringComparison.Ordinal);
        if (b < 0)
        {
            if (VacationOnly(script) is { } v)
            {
                return new SieveParsed(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(), ["vacation"] = v }, string.Empty, false);
            }

            return new SieveParsed(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray() }, StripRequires(script.Trim()), false);
        }

        var e = script.IndexOf(End, b, StringComparison.Ordinal);
        if (e < 0)
        {
            e = script.Length;
        }

        var jsonTxt = script[(b + Begin.Length)..e].Replace("*\\/", "*/", StringComparison.Ordinal);
        JsonNode? parsed = null;
        try
        {
            parsed = JsonNode.Parse(jsonTxt.Trim());
        }
        catch (JsonException)
        {
        }

        var rest = script[Math.Min(e + End.Length, script.Length)..];
        var locked = parsed is null || !MetadataOk(parsed);
        var metadata = parsed ?? new JsonObject { ["version"] = 1, ["rules"] = new JsonArray() };
        return new SieveParsed(metadata, ExternalBlocks(rest, metadata), locked);
    }

    private static List<string> Lines(string s)
    {
        var l = s.Split('\n').Select(x => x.TrimEnd('\r')).ToList();
        if (l.Count > 0 && l[^1].Length == 0)
        {
            l.RemoveAt(l.Count - 1);
        }

        return l;
    }

    private static string ExternalBlocks(string rest, JsonNode meta)
    {
        rest = string.Join('\n', Lines(rest).Where(l => !string.Equals(l.Trim(), External, StringComparison.Ordinal)));
        var names = Arr(meta, "rules").Select(r => CollapseWs(S(r, "name") ?? string.Empty).Trim()).ToList();
        var forwardOn = meta is JsonObject mo && mo["vacationForward"] is { } vf && Is(vf, "enabled", true);
        var requires = new List<string>();
        var kept = new List<string>();
        foreach (var b in ScanTopLevel(rest))
        {
            if (b.Kind is "require")
            {
                foreach (var t in RequireTokens(b.Statement))
                {
                    if (!requires.Contains(t))
                    {
                        requires.Add(t);
                    }
                }

                continue;
            }

            var raw = b.Raw;
            var lines = Lines(raw);
            string? ruleName = null;
            foreach (var l in lines)
            {
                var t = l.TrimStart();
                if (t.StartsWith('#') && t[1..].TrimStart() is var after && after.StartsWith("Rule:", StringComparison.Ordinal))
                {
                    ruleName = CollapseWs(after[5..]).Trim();
                    break;
                }
            }

            if (ruleName is not null && names.Contains(ruleName))
            {
                continue;
            }

            if (lines.Any(l => l.TrimStart() is var t && t.StartsWith('#') && t[1..].TrimStart().StartsWith("vacation auto-reply", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (forwardOn && lines.Any(l => string.Equals(l.Trim(), VacationForwardMarker, StringComparison.Ordinal)))
            {
                continue;
            }

            kept.Add(raw.Trim());
        }

        if (!kept.Any(k => HasWord(k, "include")))
        {
            requires.RemoveAll(r => r is "include");
        }

        var o = new StringBuilder();
        if (requires.Count > 0 && kept.Count > 0)
        {
            o.Append("require [").Append(string.Join(", ", requires.Select(r => $"\"{r}\""))).Append("];\n");
        }

        o.Append(string.Join("\n\n", kept));
        return o.ToString().Trim();
    }

    // ---------------- scanning ----------------

    private static bool IsAlnum(char c) => char.IsAsciiLetterOrDigit(c);

    private static int SkipString(string s, int i)
    {
        i++;
        while (i < s.Length)
        {
            if (s[i] == '\\')
            {
                i += 2;
                continue;
            }

            if (s[i] == '"')
            {
                return i + 1;
            }

            i++;
        }

        return s.Length;
    }

    private static int SkipHash(string s, int i)
    {
        while (i < s.Length && s[i] != '\n')
        {
            i++;
        }

        return i;
    }

    private static int? SkipText(string s, int i)
    {
        if (string.CompareOrdinal(s, i, "text:", 0, 5) != 0 || (i > 0 && (IsAlnum(s[i - 1]) || s[i - 1] == '_')))
        {
            return null;
        }

        var j = i + 5;
        while (j < s.Length && s[j] != '\n')
        {
            j++;
        }

        while (j < s.Length)
        {
            var ls = j + 1;
            var k = ls;
            while (k < s.Length && s[k] != '\n')
            {
                k++;
            }

            var line = s[Math.Min(ls, s.Length)..Math.Min(k, s.Length)].TrimEnd('\r');
            if (line is ".")
            {
                return k;
            }

            j = k;
        }

        return s.Length;
    }

    private static int SkipBlockComment(string s, int i)
    {
        for (var j = i + 2; j < s.Length - 1; j++)
        {
            if (s[j] == '*' && s[j + 1] == '/')
            {
                return j + 2;
            }
        }

        return s.Length;
    }

    private static bool CommentStart(string s, int i) => s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*';

    private static int SkipStatement(string s, int i)
    {
        while (i < s.Length)
        {
            if (s[i] == '"') { i = SkipString(s, i); continue; }
            if (s[i] == '#') { i = SkipHash(s, i); continue; }
            if (CommentStart(s, i)) { i = SkipBlockComment(s, i); continue; }
            if (s[i] == 't' && SkipText(s, i) is { } t) { i = t; continue; }
            if (s[i] == ';') { return i + 1; }
            i++;
        }

        return i;
    }

    private static int SkipBalanced(string s, int i, char open, char close)
    {
        var depth = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"') { i = SkipString(s, i); continue; }
            if (c == '#') { i = SkipHash(s, i); continue; }
            if (CommentStart(s, i)) { i = SkipBlockComment(s, i); continue; }
            if (c == 't' && SkipText(s, i) is { } t) { i = t; continue; }
            i++;
            if (c == open)
            {
                depth++;
            }
            else if (c == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return i;
    }

    private static int SkipIf(string s, int i)
    {
        while (i < s.Length && s[i] != '{')
        {
            if (s[i] == '"') { i = SkipString(s, i); continue; }
            if (s[i] == '(') { i = SkipBalanced(s, i, '(', ')'); continue; }
            if (s[i] == '#') { i = SkipHash(s, i); continue; }
            if (CommentStart(s, i)) { i = SkipBlockComment(s, i); continue; }
            i++;
        }

        return i >= s.Length ? i : SkipBalanced(s, i, '{', '}');
    }

    private static int SkipIfChain(string s, int i)
    {
        var end = SkipIf(s, i);
        while (true)
        {
            var j = end;
            while (j < s.Length && char.IsWhiteSpace(s[j]))
            {
                j++;
            }

            var w = j;
            while (j < s.Length && (IsAlnum(s[j]) || s[j] == '_'))
            {
                j++;
            }

            switch (s[w..j])
            {
                case "elsif":
                    end = SkipIf(s, j);
                    break;
                case "else":
                    var k = j;
                    while (k < s.Length && s[k] != '{')
                    {
                        k++;
                    }

                    if (k >= s.Length)
                    {
                        return end;
                    }

                    end = SkipBalanced(s, k, '{', '}');
                    break;
                default:
                    return end;
            }
        }
    }

    /// <summary>The top-level blocks of a script body: if/elsif/else chains stay whole, text: strings with a leading # are not comments.</summary>
    public static IReadOnlyList<SieveBlock> ScanTopLevel(string content)
    {
        var blocks = new List<SieveBlock>();
        int i = 0, seg = 0;
        while (i < content.Length)
        {
            while (i < content.Length && char.IsWhiteSpace(content[i]))
            {
                i++;
            }

            if (i >= content.Length) { break; }
            if (content[i] == '#') { i = SkipHash(content, i); continue; }
            if (CommentStart(content, i)) { i = SkipBlockComment(content, i); continue; }
            if (!(char.IsAsciiLetter(content[i]) || content[i] == '_')) { i++; continue; }
            var start = i;
            while (i < content.Length && (IsAlnum(content[i]) || content[i] == '_'))
            {
                i++;
            }

            var kind = content[start..i] switch { "require" => "require", "if" => "if", "vacation" => "vacation", _ => "other" };
            i = kind is "if" ? SkipIfChain(content, i) : SkipStatement(content, i);
            var end = Math.Min(i, content.Length);
            blocks.Add(new SieveBlock(kind, content[seg..end], content[start..end]));
            seg = end;
        }

        return blocks;
    }

    private static List<string> RequireTokens(string stmt)
    {
        var t = stmt.TrimStart();
        var body = (t.StartsWith("require", StringComparison.Ordinal) ? t[7..] : string.Empty).Trim().TrimEnd(';').Trim();
        var res = new List<string>();
        var rest = body;
        while (true)
        {
            var a = rest.IndexOf('"', StringComparison.Ordinal);
            if (a < 0) { break; }
            var b = rest.IndexOf('"', a + 1);
            if (b < 0) { break; }
            res.Add(rest[(a + 1)..b]);
            rest = rest[(b + 1)..];
        }

        return res;
    }

    private static bool HasWord(string s, string word)
    {
        bool W(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
        var i = 0;
        while ((i = s.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
        {
            var before = i > 0 && W(s[i - 1]);
            var after = i + word.Length < s.Length && W(s[i + word.Length]);
            if (!before && !after)
            {
                return true;
            }

            i += word.Length;
        }

        return false;
    }

    private static string UnescapeSieve(string s)
    {
        var o = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\')
            {
                if (i + 1 < s.Length)
                {
                    o.Append(s[++i]);
                }
            }
            else
            {
                o.Append(s[i]);
            }
        }

        return o.ToString();
    }

    private static List<(int Start, string Inner)> Quoted(string s)
    {
        var res = new List<(int, string)>();
        var i = 0;
        while (i < s.Length)
        {
            if (s[i] == '"')
            {
                var end = SkipString(s, i);
                var close = end <= s.Length && end > i + 1 && s[end - 1] == '"' ? end - 1 : end;
                var c = Math.Max(close, i + 1);
                res.Add((i, s[(i + 1)..Math.Min(c, s.Length)]));
                i = end;
            }
            else
            {
                i++;
            }
        }

        return res;
    }

    /// <summary>The vacation settings of a script that contains nothing but a vacation action (no test), else null.</summary>
    public static JsonNode? VacationOnly(string content)
    {
        if (!HasWord(content, "vacation"))
        {
            return null;
        }

        var noReq = string.Join('\n', Lines(content).Where(l =>
        {
            var t = l.Trim();
            return !(t.StartsWith("require", StringComparison.Ordinal) && t[7..].TrimStart().StartsWith('[') && t.EndsWith(';'));
        }));
        var noHash = string.Join('\n', noReq.Split('\n').Select(l =>
        {
            var h = l.IndexOf('#', StringComparison.Ordinal);
            return h >= 0 ? l[..h] : l;
        }));
        var sb = new StringBuilder();
        var rest = noHash;
        while (true)
        {
            var a = rest.IndexOf("/*", StringComparison.Ordinal);
            if (a < 0) { break; }
            sb.Append(rest, 0, a);
            var b = rest.IndexOf("*/", a + 2, StringComparison.Ordinal);
            if (b < 0)
            {
                sb.Append(rest[a..]);
                rest = string.Empty;
                break;
            }

            rest = rest[(b + 2)..];
        }

        sb.Append(rest);
        var stripped = sb.ToString().Trim();
        var strings = Quoted(stripped);
        var structural = new StringBuilder();
        var last = 0;
        foreach (var (start, inner) in strings)
        {
            structural.Append(stripped, last, start - last).Append("\"\"");
            last = Math.Min(start + inner.Length + 2, stripped.Length);
        }

        structural.Append(stripped[Math.Min(last, stripped.Length)..]);
        var st = structural.ToString();
        if (ControlWords.Any(w => HasWord(st, w)) || !HasWord(st, "vacation"))
        {
            return null;
        }

        var subject = string.Empty;
        var si = stripped.IndexOf(":subject", StringComparison.Ordinal);
        if (si >= 0)
        {
            var after = stripped[(si + 8)..].TrimStart();
            if (after.StartsWith('"') && Quoted(after).Count > 0)
            {
                subject = UnescapeSieve(Quoted(after)[0].Inner);
            }
        }

        string? mime = null;
        var ci = stripped.IndexOf("Content-Transfer-Encoding:", StringComparison.Ordinal);
        if (ci >= 0)
        {
            var after = stripped[ci..];
            var nl = after.IndexOf('\n', StringComparison.Ordinal);
            if (nl >= 0)
            {
                var tail = after[(nl + 1)..];
                string? body = tail.StartsWith("\r\n", StringComparison.Ordinal) ? tail[2..] : tail.StartsWith('\n') ? tail[1..] : null;
                var q = body?.IndexOf('"', StringComparison.Ordinal) ?? -1;
                if (body is not null && q >= 0 && body[q..].Contains(';', StringComparison.Ordinal))
                {
                    mime = body[..q].Trim();
                }
            }
        }

        var text = mime ?? (strings.Count > 0 ? UnescapeSieve(strings[^1].Inner) : string.Empty);
        return new JsonObject { ["isEnabled"] = true, ["subject"] = subject, ["textBody"] = text };
    }

    private static bool MetadataOk(JsonNode m)
    {
        if (m is not JsonObject o)
        {
            return false;
        }

        if (!(o["version"] is JsonValue ver && ver.TryGetValue<int>(out var vn) && vn is 1 or 2))
        {
            return false;
        }

        if (o.Any(kv => !KnownKeys.Contains(kv.Key)))
        {
            return false;
        }

        if (o["rules"] is not JsonArray rules)
        {
            return false;
        }

        bool BoundOk(JsonNode? x, string k) => !Has(x, k) || (S(x, k) is { } s && PeriodBoundaryOf(s) is not null);
        if (!rules.All(r => BoundOk(r, "activeFrom") && BoundOk(r, "activeUntil")))
        {
            return false;
        }

        if (o["vacationForward"] is { } f && !ValidForward(f))
        {
            return false;
        }

        if (o["vacationAudience"] is { } a && !ValidAudience(a))
        {
            return false;
        }

        return !o.ContainsKey("vacation") || o["vacation"] is JsonObject;
    }

    private static string StripRequires(string s) =>
        string.Join('\n', Lines(s).Where(l => !l.TrimStart().StartsWith("require ", StringComparison.Ordinal))).Trim();

    // ---------------- generation ----------------

    private static string Esc(string v) => v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private static List<string> Values(JsonNode? v)
    {
        if (v is JsonArray a)
        {
            return a.Select(x => x is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : null).Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).ToList();
        }

        return v is JsonValue j && j.TryGetValue<string>(out var str) && str.Length > 0 ? [str] : [];
    }

    private static string Arg(List<string> vals, Func<string, string> f) =>
        vals.Count == 1 ? $"\"{Esc(f(vals[0]))}\"" : $"[{string.Join(", ", vals.Select(v => $"\"{Esc(f(v))}\""))}]";

    private static string HeaderName(JsonNode c)
    {
        var f = S(c, "field") ?? "from";
        return f switch
        {
            "from" => "From",
            "to" => "To",
            "cc" => "Cc",
            "subject" => "Subject",
            _ => S(c, "headerName")?.Trim() is { Length: > 0 } h ? h : "X-Unknown",
        };
    }

    private static string Condition(JsonNode c, List<string> req)
    {
        var field = S(c, "field") ?? "from";
        var cmp = S(c, "comparator") ?? "contains";
        if (field is "all")
        {
            return "true";
        }

        if (field is "size")
        {
            var v = c["value"];
            if (v is JsonArray arr)
            {
                v = arr.Count > 0 ? arr[0] : null;
            }

            var raw = v is JsonValue jv ? (jv.TryGetValue<string>(out var s) ? s : jv.ToJsonString()) : string.Empty;
            raw = raw.Trim();
            var digits = raw.TrimEnd('K', 'M', 'G', 'k', 'm', 'g');
            var ok = digits.Length > 0 && digits.All(char.IsAsciiDigit) && raw.Length - digits.Length <= 1;
            return $"size {(cmp is "greater_than" ? ":over" : ":under")} {(ok ? raw : "0")}";
        }

        if (cmp is "regex")
        {
            req.Add("regex");
        }

        var vals = Values(c["value"]);
        if (field is "body")
        {
            req.Add("body");
            var m = cmp switch { "is" => ":is", "regex" => ":regex", _ => ":contains" };
            return $"body {m} {Arg(vals, s => s)}";
        }

        if (field is "attachment")
        {
            req.Add("mime");
            if (cmp is "has_any")
            {
                return "header :mime :anychild :contains \"Content-Disposition\" \"attachment\"";
            }

            var exts = vals.Select(v => v.TrimStart('.', '*').Trim()).Where(v => v.Length > 0).ToList();
            return $"header :mime :anychild :matches [\"Content-Disposition\", \"Content-Type\"] {Arg(exts, e => $"*.{e}*")}";
        }

        var h = Esc(HeaderName(c));
        if (cmp is "address_is" or "domain_is" && field is "from" or "to" or "cc")
        {
            return $"address {(cmp is "domain_is" ? ":domain " : string.Empty)}:is \"{h}\" {Arg(vals, s => s)}";
        }

        return cmp switch
        {
            "not_contains" => $"not header :contains \"{h}\" {Arg(vals, s => s)}",
            "is" => $"header :is \"{h}\" {Arg(vals, s => s)}",
            "not_is" => $"not header :is \"{h}\" {Arg(vals, s => s)}",
            "starts_with" => $"header :matches \"{h}\" {Arg(vals, s => s + "*")}",
            "ends_with" => $"header :matches \"{h}\" {Arg(vals, s => "*" + s)}",
            "matches" => $"header :matches \"{h}\" {Arg(vals, s => s)}",
            "regex" => $"header :regex \"{h}\" {Arg(vals, s => s)}",
            _ => $"header :contains \"{h}\" {Arg(vals, s => s)}",
        };
    }

    private static List<string> Actions(JsonArray acts, bool useMbid, List<string> req)
    {
        static bool IsFlag(JsonNode? a) => S(a, "type") is "mark_read" or "star" or "add_label";
        var ordered = acts.Where(IsFlag).Concat(acts.Where(a => !IsFlag(a))).ToList();

        string Target(JsonNode a)
        {
            var path = $"\"{Esc(S(a, "value") ?? string.Empty)}\"";
            var id = S(a, "mailboxId");
            if (useMbid && !string.IsNullOrEmpty(id))
            {
                req.Add("mailbox");
                req.Add("mailboxid");
                return $":mailboxid \"{Esc(id)}\" {path}";
            }

            return path;
        }

        var res = new List<string>();
        foreach (var a in ordered)
        {
            var v = Esc(S(a, "value") ?? string.Empty);
            switch (S(a, "type"))
            {
                case "move":
                    req.Add("fileinto");
                    res.Add($"fileinto {Target(a!)};");
                    break;
                case "copy":
                    req.Add("fileinto");
                    req.Add("copy");
                    res.Add($"fileinto :copy {Target(a!)};");
                    break;
                case "forward":
                    var keep = Is(a, "keepCopy", true);
                    if (keep)
                    {
                        req.Add("copy");
                    }

                    res.Add($"redirect {(keep ? ":copy " : string.Empty)}\"{v}\";");
                    break;
                case "mark_read":
                    req.Add("imap4flags");
                    res.Add("addflag \"\\\\Seen\";");
                    break;
                case "star":
                    req.Add("imap4flags");
                    res.Add("addflag \"\\\\Flagged\";");
                    break;
                case "add_label":
                    req.Add("imap4flags");
                    res.Add($"addflag \"$label:{v}\";");
                    break;
                case "discard":
                    res.Add("discard;");
                    break;
                case "reject":
                    req.Add("reject");
                    res.Add($"reject \"{v}\";");
                    break;
                case "keep":
                    req.Add("fileinto");
                    res.Add("fileinto \"INBOX\";");
                    break;
                case "stop":
                    res.Add("stop;");
                    break;
            }
        }

        return res;
    }

    /// <summary>The UTC (date, time) of an ISO timestamp with seconds and a zone, or null.</summary>
    public static PeriodBoundary? PeriodBoundaryOf(string value)
    {
        bool Dig(int from, int to) => Enumerable.Range(from, to - from).All(i => i < value.Length && char.IsAsciiDigit(value[i]));
        char? At(int i) => i < value.Length ? value[i] : null;
        if (!(Dig(0, 4) && At(4) == '-' && Dig(5, 7) && At(7) == '-' && Dig(8, 10) && At(10) == 'T' && Dig(11, 13) && At(13) == ':' && Dig(14, 16)))
        {
            return null;
        }

        var i = 16;
        var hasSecs = At(16) == ':';
        if (hasSecs)
        {
            if (!Dig(17, 19))
            {
                return null;
            }

            i = 19;
            if (At(i) == '.')
            {
                var n = 0;
                while (i + 1 + n < value.Length && char.IsAsciiDigit(value[i + 1 + n]))
                {
                    n++;
                }

                if (n is < 1 or > 3)
                {
                    return null;
                }

                i += 1 + n;
            }
        }

        var zone = value[i..];
        var zoneOk = zone is "Z" || (zone.Length == 6 && zone[0] is '+' or '-' && char.IsAsciiDigit(zone[1]) && char.IsAsciiDigit(zone[2]) && zone[3] == ':' && char.IsAsciiDigit(zone[4]) && char.IsAsciiDigit(zone[5]));
        if (!zoneOk)
        {
            return null;
        }

        var full = hasSecs ? value : $"{value[..16]}:00{zone}";
        if (!DateTimeOffset.TryParse(full, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
        {
            return null;
        }

        var iso = t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        return iso.Length != 19 ? null : new PeriodBoundary(iso[..10], iso[11..19]);
    }

    /// <summary>The UTC date and time tests for activeFrom and activeUntil (none for an empty object), or null when one is not a valid timestamp.</summary>
    public static IReadOnlyList<string>? PeriodTests(JsonNode period)
    {
        var t = new List<string>();
        if (Has(period, "activeFrom"))
        {
            if (S(period, "activeFrom") is not { } f || PeriodBoundaryOf(f) is not { } b)
            {
                return null;
            }

            t.Add($"anyof({Now} :value \"gt\" \"date\" \"{b.Date}\", allof({Now} :is \"date\" \"{b.Date}\", {Now} :value \"ge\" \"time\" \"{b.Time}\"))");
        }

        if (Has(period, "activeUntil"))
        {
            if (S(period, "activeUntil") is not { } u || PeriodBoundaryOf(u) is not { } b)
            {
                return null;
            }

            t.Add($"anyof({Now} :value \"lt\" \"date\" \"{b.Date}\", allof({Now} :is \"date\" \"{b.Date}\", {Now} :value \"le\" \"time\" \"{b.Time}\"))");
        }

        return t;
    }

    private static bool HasPeriod(JsonNode x) => Has(x, "activeFrom") || Has(x, "activeUntil");

    private static bool ValidEmail(string e)
    {
        if (e.Length == 0 || e.Length > 254 || e.IndexOfAny(['\r', '\n', '\0', '<', '>']) >= 0)
        {
            return false;
        }

        var at = e.IndexOf('@', StringComparison.Ordinal);
        if (at < 0)
        {
            return false;
        }

        var l = e[..at];
        var d = e[(at + 1)..];
        static bool Label(string s) => s.Length is > 0 and <= 63 && s.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && !s.StartsWith('-') && !s.EndsWith('-');
        return l.Length > 0 && l.All(c => char.IsAsciiLetterOrDigit(c) || ".!#$%&'*+/=?^_`{|}~-".Contains(c, StringComparison.Ordinal)) && d.Split('.').All(Label);
    }

    private static bool ValidDomain(string d)
    {
        var labels = d.Split('.');
        return labels.Length >= 2 && labels.All(l => l.Length > 0 && l.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') && !l.StartsWith('-') && !l.EndsWith('-'));
    }

    /// <summary>Bulwark isValidVacationForward.</summary>
    private static bool ValidForward(JsonNode f)
    {
        bool B(string k) => !Has(f, k) || (S(f, k) is { } s && PeriodBoundaryOf(s) is not null);
        return f is JsonObject o && o["enabled"] is JsonValue e && e.TryGetValue<bool>(out _) && o["keepCopy"] is JsonValue k && k.TryGetValue<bool>(out _)
            && S(f, "to") is { } to && ValidEmail(to) && B("activeFrom") && B("activeUntil");
    }

    /// <summary>Bulwark isValidVacationAudience.</summary>
    private static bool ValidAudience(JsonNode a) =>
        S(a, "only") is "internal" or "external" && a["domains"] is JsonArray d && d.Count > 0 && d.All(x => x is JsonValue v && v.TryGetValue<string>(out var s) && ValidDomain(s));

    private static JsonObject Pick(JsonNode v, params string[] keys)
    {
        var o = new JsonObject();
        foreach (var k in keys)
        {
            if (v is JsonObject src && src.ContainsKey(k))
            {
                o[k] = src[k]?.DeepClone();
            }
        }

        return o;
    }

    private static string CollapseWs(string s)
    {
        var o = new StringBuilder();
        var ws = false;
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!ws)
                {
                    o.Append(' ');
                }

                ws = true;
            }
            else
            {
                o.Append(c);
                ws = false;
            }
        }

        return o.ToString();
    }

    /// <summary>The Sieve text for rules plus an external part (metadata version 1 shape).</summary>
    public static string Generate(IReadOnlyList<JsonNode> rules, string external) =>
        GenerateMeta(new JsonObject { ["version"] = 1, ["rules"] = new JsonArray(rules.Select(r => r.DeepClone()).ToArray()) }, external, []);

    /// <summary>The metadata with its rules replaced, extras (vacation, forward, audience) kept.</summary>
    public static JsonNode WithRules(JsonNode metadata, IReadOnlyList<JsonNode> rules)
    {
        var m = metadata is JsonObject o ? (JsonObject)o.DeepClone() : new JsonObject { ["version"] = 1 };
        m["rules"] = new JsonArray(rules.Select(r => r.DeepClone()).ToArray());
        return m;
    }

    /// <summary>The Sieve text for the whole metadata (rules, vacation, forward, audience, period tests) given the server's extensions.</summary>
    public static string GenerateMeta(JsonNode metadata, string external, IReadOnlyList<string> serverExtensions)
    {
        bool HasExt(string e) => serverExtensions.Contains(e, StringComparer.Ordinal);
        var guardOk = HasExt("spamtestplus") && HasExt("relational");
        var useMbid = HasExt("mailboxid");
        var rules = Arr(metadata, "rules").Where(r => r is not null).Select(r => r!).ToList();
        var vacation = metadata["vacation"] is { } vv && Is(vv, "isEnabled", true) ? vv : null;
        var includeVac = Is(metadata, "includeVacation", true);
        JsonObject? forward = metadata["vacationForward"] is { } fw && ValidForward(fw) ? Pick(fw, "enabled", "to", "keepCopy", "activeFrom", "activeUntil") : null;
        JsonObject? audience = metadata["vacationAudience"] is { } au && ValidAudience(au) ? Pick(au, "only", "domains") : null;

        static bool UsesRegex(JsonNode r) => Arr(r, "conditions").Any(c => IsStr(c, "comparator", "regex") && S(c, "field") is "from" or "to" or "cc" or "subject" or "header" or "body");
        var v2 = rules.Any(HasPeriod) || rules.Any(UsesRegex) || (forward is not null && Is(forward, "enabled", true)) || audience is not null;
        var m = new JsonObject { ["version"] = v2 ? 2 : 1, ["rules"] = new JsonArray(rules.Select(r => r.DeepClone()).ToArray()) };
        if (vacation is not null) { m["vacation"] = vacation.DeepClone(); }
        if (includeVac) { m["includeVacation"] = true; }
        if (forward is not null) { m["vacationForward"] = forward.DeepClone(); }
        if (audience is not null) { m["vacationAudience"] = audience.DeepClone(); }
        var metaTxt = Json(m).Replace("*/", "*\\/", StringComparison.Ordinal);

        var req = new List<string>();
        if (vacation is not null) { req.Add("vacation"); }
        var ruleLines = new List<string>();
        foreach (var r in rules.Where(r => !Is(r, "enabled", false)))
        {
            var condsV = Arr(r, "conditions");
            var actsV = Arr(r, "actions");
            var all = !IsStr(r, "matchType", "any");
            var guarded = guardOk && !Is(r, "includeSpam", true) && actsV.Any(x => S(x, "type") is "move" or "copy");
            if (guarded) { req.AddRange(SpamGuardRequires); }
            var period = PeriodTests(r);
            if (period is { Count: > 0 }) { req.AddRange(PeriodRequires); }
            var conds = condsV.Select(c => Condition(c!, req)).ToList();
            var acts = Actions(actsV, useMbid, req);
            if (conds.Count == 0 || actsV.Count == 0) { continue; }
            var badHeader = condsV.Where(c => IsStr(c, "field", "header")).Select(c => HeaderName(c!)).Any(h => h.Length == 0 || !h.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-'));
            if (badHeader) { continue; }
            if (period is null) { continue; }
            var test = conds.Count == 1 ? conds[0] : $"{(all ? "allof" : "anyof")}({string.Join(", ", conds)})";
            if (guarded)
            {
                test = conds.Count > 1 && all ? $"allof({string.Join(", ", conds)}, {SpamGuard})" : $"allof({test}, {SpamGuard})";
            }

            if (period.Count > 0) { test = $"allof({string.Join(", ", period)}, {test})"; }
            if (Is(r, "stopProcessing", true) && !acts.Contains("stop;")) { acts.Add("stop;"); }
            ruleLines.Add(string.Empty);
            ruleLines.Add($"# Rule: {CollapseWs(S(r, "name") ?? string.Empty)}");
            ruleLines.Add($"if {test} {{");
            ruleLines.AddRange(acts.Select(a => $"    {a}"));
            ruleLines.Add("}");
        }

        if (includeVac) { req.Add("include"); }
        if (includeVac && audience is not null) { req.Add("envelope"); }
        var fwd = forward is not null && Is(forward, "enabled", true) ? forward : null;
        var fwdPeriod = fwd is null ? null : PeriodTests(fwd);
        if (fwd is not null && fwdPeriod is not null)
        {
            if (Is(fwd, "keepCopy", true)) { req.Add("copy"); }
            if (fwdPeriod.Count > 0) { req.AddRange(PeriodRequires); }
            if (guardOk) { req.AddRange(SpamGuardRequires); }
        }

        var extReq = Lines(external)
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("require ", StringComparison.Ordinal))
            .SelectMany(l => l["require ".Length..].Trim().TrimEnd(';').Trim().Trim('[', ']').Split(',').Select(s => s.Trim().Trim('"')));
        var allReq = req.Concat(extReq).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();

        var lines = new List<string> { Begin, metaTxt, End, string.Empty };
        if (allReq.Count > 0)
        {
            lines.Add($"require [{string.Join(", ", allReq.Select(s => $"\"{s}\""))}];");
        }

        if (includeVac)
        {
            lines.Add(string.Empty);
            lines.Add("# Vacation auto-reply");
            const string inc = "include :personal :optional \"vacation\";";
            if (audience is not null)
            {
                var doms = string.Join(", ", Arr(audience, "domains").Select(x => $"\"{Esc(x is JsonValue j && j.TryGetValue<string>(out var s) ? s : string.Empty)}\""));
                lines.Add($"if {(IsStr(audience, "only", "external") ? "not " : string.Empty)}envelope :domain :is \"from\" [{doms}] {{");
                lines.Add($"    {inc}");
                lines.Add("}");
            }
            else
            {
                lines.Add(inc);
            }
        }

        if (vacation is not null)
        {
            lines.Add(string.Empty);
            lines.Add("# Vacation auto-reply");
            var subject = S(vacation, "subject") is { Length: > 0 } sj ? $":subject \"{Esc(sj)}\" " : string.Empty;
            lines.Add($"vacation {subject}\"{Esc(S(vacation, "textBody") ?? string.Empty)}\";");
        }

        if (fwd is not null && fwdPeriod is not null)
        {
            var tests = fwdPeriod.ToList();
            if (guardOk) { tests.Add(SpamGuard); }
            var c = tests.Count switch { 0 => "true", 1 => tests[0], _ => $"allof({string.Join(", ", tests)})" };
            var kc = Is(fwd, "keepCopy", true);
            lines.Add(string.Empty);
            lines.Add(VacationForwardMarker);
            lines.Add($"if {c} {{");
            lines.Add($"    redirect {(kc ? ":copy " : string.Empty)}\"{Esc(S(fwd, "to") ?? string.Empty)}\";");
            if (!kc) { lines.Add("    stop;"); }
            lines.Add("}");
        }

        lines.AddRange(ruleLines);
        var ext = StripRequires(external);
        if (ext.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add(External);
            lines.Add(ext);
        }

        lines.Add(string.Empty);
        return string.Join('\n', lines);
    }

    /// <summary>A rule in Bulwark's shape with one condition and one action.</summary>
    public static JsonNode Rule(string name, string field, string comparator, string value, string action, string actionValue, bool stop)
    {
        var id = "bw-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));
        var (cmp, val) = field is "attachment" ? ("has_any", string.Empty) : field is "all" ? ("any", string.Empty) : (comparator, value);
        var act = new JsonObject { ["type"] = action };
        if (actionValue.Trim().Length > 0)
        {
            act["value"] = actionValue.Trim();
        }

        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name.Trim().Length == 0 ? "Rule" : name.Trim(),
            ["enabled"] = true,
            ["matchType"] = "all",
            ["conditions"] = new JsonArray(new JsonObject { ["field"] = field, ["comparator"] = cmp, ["value"] = val }),
            ["actions"] = new JsonArray(act),
            ["stopProcessing"] = stop,
        };
    }

    /// <summary>The UTC ISO instant (.000Z) of a form day: the start of the day, or its end; empty gives a null value, a bad format a failure.</summary>
    public static Result<string?> DayBoundary(string day, bool end)
    {
        day = day.Trim();
        if (day.Length == 0)
        {
            return Result<string?>.Ok(null);
        }

        var bad = Result<string?>.Fail(new ValidationError($"\"{day}\" is not a date (YYYY-MM-DD)."));
        DateTime t;
        var withT = day.Length > 10 && day[10] == ' ' ? string.Concat(day.AsSpan(0, 10), "T", day.AsSpan(11)) : day;
        if (!DateTime.TryParseExact(withT, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
        {
            if (!DateTime.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return bad;
            }

            t = end ? d.AddHours(23).AddMinutes(59) : d;
        }

        DateTime utc;
        try
        {
            utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(t, DateTimeKind.Unspecified), TimeZoneInfo.Local);
        }
        catch (ArgumentException)
        {
            return Result<string?>.Fail(new ValidationError("bad date"));
        }

        return Result<string?>.Ok(utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
    }
}
