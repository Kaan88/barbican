// Result references of the fake server: RFC 8620 §3.7 (an argument `#name` holds {resultOf, name, path}; the path is a JSON Pointer
// evaluated on the earlier response's arguments, where `*` maps over an array and nested arrays are flattened). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.FakeJmap;

/// <summary>Resolves `#` arguments against earlier responses.</summary>
public static class ResultReferences
{
    /// <summary>The arguments with every `#name` replaced by its value; null when a reference cannot be resolved.</summary>
    public static JsonObject? Resolve(JsonObject args, IReadOnlyList<JsonArray> earlier)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(earlier);
        var output = new JsonObject();
        foreach (var (key, value) in args)
        {
            if (!key.StartsWith('#'))
            {
                output[key] = value?.DeepClone();
                continue;
            }

            if (value is not JsonObject r || (string?)r["resultOf"] is not { } of || (string?)r["name"] is not { } name || (string?)r["path"] is not { } path)
            {
                return null;
            }

            var source = earlier.LastOrDefault(x => string.Equals((string?)x[2], of, StringComparison.Ordinal));
            if (source is null || !string.Equals((string?)source[0], name, StringComparison.Ordinal))
            {
                return null;
            }

            var resolved = Evaluate(source[1], path);
            if (resolved is null)
            {
                return null;
            }

            output[key[1..]] = resolved;
        }

        return output;
    }

    /// <summary>A JSON Pointer with the `*` extension; null when the path does not exist.</summary>
    public static JsonNode? Evaluate(JsonNode? node, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0)
        {
            return node?.DeepClone();
        }

        var tokens = path.Split('/')[1..].Select(t => t.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        return Walk(node, tokens, 0);
    }

    private static JsonNode? Walk(JsonNode? node, string[] tokens, int i)
    {
        if (i == tokens.Length)
        {
            return node?.DeepClone();
        }

        var t = tokens[i];
        switch (node)
        {
            case JsonArray a when string.Equals(t, "*", StringComparison.Ordinal):
                var flat = new JsonArray();
                foreach (var item in a)
                {
                    var v = Walk(item, tokens, i + 1);
                    if (v is JsonArray inner)
                    {
                        foreach (var x in inner)
                        {
                            flat.Add(x?.DeepClone());
                        }
                    }
                    else if (v is not null)
                    {
                        flat.Add(v);
                    }
                }

                return flat;
            case JsonArray a when int.TryParse(t, System.Globalization.CultureInfo.InvariantCulture, out var n) && n >= 0 && n < a.Count:
                return Walk(a[n], tokens, i + 1);
            case JsonObject o when o.TryGetPropertyValue(t, out var child):
                return Walk(child, tokens, i + 1);
            default:
                return null;
        }
    }
}
