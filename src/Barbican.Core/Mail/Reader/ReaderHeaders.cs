// Mirrors: Rust reader.rs:27 header, :33 headers_all, :38 unfold; RFC 5322 section 2.2.3 (unfolding); Bulwark lib/email-headers.ts:625-660
// (getHeader: the first match, case-insensitive, trimmed). Typed overloads for the reader's EmailHeader list (RFC 8621 §4.1.3 headers).

using System.Text;
using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;
using Barbican.Core.Mail.Model;

namespace Barbican.Core.Mail.Reader;

/// <summary>Reads header values from the JMAP header list ({"name", "value"} objects).</summary>
public static class ReaderHeaders
{
    /// <summary>The first value of a header (name case-insensitive), unfolded and trimmed, or null.</summary>
    public static string? Header(JsonArray headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(name);
        var h = headers.FirstOrDefault(h => string.Equals(h.Str("name"), name, StringComparison.OrdinalIgnoreCase));
        return h.Str("value") is { } v ? Unfold(v) : null;
    }

    /// <summary>Every value of a header (name case-insensitive), in order.</summary>
    public static IReadOnlyList<string> HeadersAll(JsonArray headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(name);
        return [.. headers.Where(h => string.Equals(h.Str("name"), name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Str("value")).OfType<string>().Select(Unfold)];
    }

    /// <summary>The first value of a header of a fetched message, unfolded and trimmed, or null.</summary>
    public static string? Header(IReadOnlyList<EmailHeader> headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(name);
        var h = headers.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase));
        return h is null ? null : Unfold(h.Value);
    }

    /// <summary>Every value of a header of a fetched message, unfolded, in order.</summary>
    public static IReadOnlyList<string> HeadersAll(IReadOnlyList<EmailHeader> headers, string name)
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(name);
        return [.. headers.Where(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)).Select(h => Unfold(h.Value))];
    }

    /// <summary>The header list as JSON objects (the shape the ported Rust functions read).</summary>
    public static JsonArray ToJson(IReadOnlyList<EmailHeader> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return new JsonArray([.. headers.Select(h => (JsonNode)new JsonObject { ["name"] = h.Name, ["value"] = h.Value })]);
    }

    /// <summary>CR dropped, a line break and the blanks after it become one space, then trimmed.</summary>
    public static string Unfold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                while (i + 1 < value.Length && value[i + 1] is ' ' or '\t')
                {
                    i++;
                }

                sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString().Trim();
    }
}
