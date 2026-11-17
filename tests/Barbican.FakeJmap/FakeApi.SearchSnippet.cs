// Mirrors: RFC 8621 section 5 (SearchSnippet/get: emailIds + filter, subject and preview with <mark> around the hits, null when nothing matched); Stalwart crates/jmap/src/email/snippet.rs.
// The fake marks every case-insensitive occurrence of the text, subject and body terms of the filter.

using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

public sealed partial class FakeApi
{
    private static readonly string[] SnippetKeys = ["text", "subject", "body"];

    private static void Terms(JsonNode? filter, List<string> into)
    {
        if (filter is not JsonObject f)
        {
            return;
        }

        foreach (var c in f["conditions"] as JsonArray ?? [])
        {
            Terms(c, into);
        }

        foreach (var key in SnippetKeys)
        {
            if ((string?)f[key] is { Length: > 0 } t)
            {
                into.AddRange(t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
        }
    }

    private static string? Mark(string? text, List<string> terms)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var marks = new bool[text.Length];
        var any = false;
        foreach (var t in terms)
        {
            for (var i = text.IndexOf(t, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(t, i + t.Length, StringComparison.OrdinalIgnoreCase))
            {
                Array.Fill(marks, true, i, t.Length);
                any = true;
            }
        }

        if (!any)
        {
            return null;
        }

        var sb = new StringBuilder();
        var open = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (marks[i] != open)
            {
                sb.Append(marks[i] ? "<mark>" : "</mark>");
                open = marks[i];
            }

            sb.Append(WebUtility.HtmlEncode(text[i].ToString()));
        }

        if (open)
        {
            sb.Append("</mark>");
        }

        return sb.ToString();
    }

    private (string? Name, JsonObject Args) SearchSnippetGet(JsonObject args)
    {
        var terms = new List<string>();
        Terms(args["filter"], terms);
        var ids = (args["emailIds"] as JsonArray)?.Select(x => (string?)x).OfType<string>().ToList() ?? [];
        var list = new JsonArray();
        var notFound = new JsonArray();
        var found = store.Emails(ids).ToDictionary(e => (string)e["id"]!, StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (!found.TryGetValue(id, out var e))
            {
                notFound.Add(id);
                continue;
            }

            list.Add(new JsonObject { ["emailId"] = id, ["subject"] = Mark((string?)e["subject"], terms), ["preview"] = Mark((string?)e["preview"], terms) });
        }

        return (null, new JsonObject { ["accountId"] = options.AccountId, ["list"] = list, ["notFound"] = notFound.Count > 0 ? notFound : null });
    }
}
