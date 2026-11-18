// The text rules of one property line: RFC 6868 caret decoding of parameters, quoted splitting, group prefixes, Apple labels, value escapes (one pass; "\:" is not standard but Google writes it), quoted-printable. Ports of Rust vcard.rs:12-170.

using System.Globalization;
using System.Text;

namespace Barbican.Core.Contacts;

public static partial class VCardParser
{
    private static readonly string[] TypeWords = ["WORK", "HOME", "CELL", "FAX", "VOICE", "PREF", "PAGER", "VIDEO", "TEXT", "TEXTPHONE", "INTERNET"];

    /// <summary>RFC 6868 caret decoding of a parameter value.</summary>
    internal static string DecodeParam(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '^' && i + 1 < s.Length)
            {
                var n = s[i + 1];
                if (n == 'n')
                {
                    _ = sb.Append('\n');
                    i++;
                    continue;
                }

                if (n == '^')
                {
                    _ = sb.Append('^');
                    i++;
                    continue;
                }

                if (n == '\'')
                {
                    _ = sb.Append('"');
                    i++;
                    continue;
                }
            }

            _ = sb.Append(s[i]);
        }

        return sb.ToString();
    }

    internal static string EncodeParam(string s) => s.Replace("^", "^^", StringComparison.Ordinal).Replace("\"", "^'", StringComparison.Ordinal).Replace("\n", "^n", StringComparison.Ordinal);

    internal static List<string> SplitQuoted(string s, char delimiter)
    {
        var parts = new List<string>();
        var buf = new StringBuilder();
        var quoted = false;
        foreach (var c in s)
        {
            if (c == '"')
            {
                quoted = !quoted;
                _ = buf.Append(c);
            }
            else if (c == delimiter && !quoted)
            {
                parts.Add(buf.ToString());
                _ = buf.Clear();
            }
            else
            {
                _ = buf.Append(c);
            }
        }

        parts.Add(buf.ToString());
        return parts;
    }

    private static int? ValueColon(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            if (line[i] == '"')
            {
                quoted = !quoted;
            }
            else if (line[i] == ':' && !quoted)
            {
                return i;
            }
        }

        return null;
    }

    private static (string Group, string Key) SplitGroup(string key)
    {
        var dot = key.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            var before = key[..dot];
            if (!before.Contains(';', StringComparison.Ordinal) && before.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            {
                return (before.ToLowerInvariant(), key[(dot + 1)..]);
            }
        }

        return (string.Empty, key);
    }

    private static string AppleLabel(string v) => v.StartsWith("_$!<", StringComparison.Ordinal) && v.EndsWith(">!$_", StringComparison.Ordinal) && v.Length >= 8 ? v[4..^4] : v;

    private static string StripScheme(string v, string scheme) =>
        v.Length >= scheme.Length + 1 && v.StartsWith(scheme + ":", StringComparison.OrdinalIgnoreCase) ? v[(scheme.Length + 1)..] : v;

    /// <summary>Text value escapes: \n, \\, \, \; and \: .</summary>
    internal static string DecodeValue(string raw)
    {
        if (!raw.Contains('\\', StringComparison.Ordinal))
        {
            return raw;
        }

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] == '\\' && i + 1 < raw.Length)
            {
                var n = raw[i + 1];
                if (n is 'n' or 'N')
                {
                    _ = sb.Append('\n');
                    i++;
                    continue;
                }

                if (n is '\\' or ',' or ';' or ':')
                {
                    _ = sb.Append(n);
                    i++;
                    continue;
                }
            }

            _ = sb.Append(raw[i]);
        }

        return sb.ToString();
    }

    internal static string EncodeValue(string v) =>
        v.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    private static string DecodeQuotedPrintable(string input)
    {
        var bytes = new List<byte>(input.Length);
        var i = 0;
        while (i < input.Length)
        {
            if (input[i] == '=' && i + 2 < input.Length && Uri.IsHexDigit(input[i + 1]) && Uri.IsHexDigit(input[i + 2]))
            {
                bytes.Add(Convert.ToByte(input.Substring(i + 1, 2), 16));
                i += 3;
                continue;
            }

            // any other character keeps its own UTF-8 bytes (a surrogate pair stays whole)
            var width = char.IsHighSurrogate(input[i]) && i + 1 < input.Length ? 2 : 1;
            bytes.AddRange(Encoding.UTF8.GetBytes(input.Substring(i, width)));
            i += width;
        }

        return Encoding.UTF8.GetString([.. bytes]);
    }

    private static Dictionary<string, string> ParseParams(string s)
    {
        var p = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in SplitQuoted(s, ';'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var eq = part.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                var name = part[..eq].ToUpperInvariant();
                var raw = part[(eq + 1)..];
                if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                {
                    raw = raw[1..^1];
                }

                var value = DecodeParam(raw);

                // repeated TYPE params add up (TYPE=WORK;TYPE=VOICE)
                p[name] = string.Equals(name, "TYPE", StringComparison.Ordinal) && p.TryGetValue("TYPE", out var old) ? old + "," + value : value;
            }
            else
            {
                var up = part.ToUpperInvariant();
                if (up is "QUOTED-PRINTABLE" or "BASE64")
                {
                    p["ENCODING"] = up;
                }
                else if (TypeWords.Contains(up, StringComparer.Ordinal))
                {
                    p["TYPE"] = p.TryGetValue("TYPE", out var old) ? old + "," + up : up;
                }
            }
        }

        return p;
    }

    /// <summary>True when every character is of the base64 alphabet (or padding): a check that allocates nothing, so a megabyte of junk costs one pass.</summary>
    internal static bool IsBase64Text(string text)
    {
        foreach (var c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string? PStr(IReadOnlyDictionary<string, string> p, string key) => p.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static int? Pref(IReadOnlyDictionary<string, string> p)
    {
        if (PStr(p, "PREF") is { } text && int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return n;
        }

        return PStr(p, "TYPE") is { } t && t.ToUpperInvariant().Split(',').Any(x => string.Equals(x.Trim(), "PREF", StringComparison.Ordinal)) ? 1 : null;
    }
}
