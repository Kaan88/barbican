// Mirrors: Rust jmap.rs:96 read_only (the batches that may be replayed after 429/503); PORT-PLAN.md §4.6 (the ReadOnly allow-list:
// /get, /query, /changes, /queryChanges, /parse, /validate, SearchSnippet/get; Blob download is not a method call);
// RFC 8620 §3.6.1 (a retry is only safe for calls that change nothing). Owner jmap.

using System.Text.Json.Nodes;
using Barbican.Core.Jmap.Json;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Decides whether a method, or a whole batch, only reads.</summary>
public static class ReadOnlyGuard
{
    private static readonly string[] ReadSuffixes = ["/get", "/query", "/changes", "/queryChanges", "/parse", "/validate"];

    /// <summary>True for a method that changes nothing on the server (<c>Core/echo</c> included).</summary>
    public static bool IsReadMethod(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return string.Equals(method, "Core/echo", StringComparison.Ordinal)
            || ReadSuffixes.Any(s => method.EndsWith(s, StringComparison.Ordinal));
    }

    /// <summary>True when every call is a read (get, query, changes, queryChanges, parse, validate, echo).</summary>
    public static bool IsReadOnly(JsonArray calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        return calls.All(c => c is JsonArray a && a.Count > 0 && a[0].AsString() is { } m && IsReadMethod(m));
    }

    /// <summary>The first method of the calls that is not a read, or null when all read.</summary>
    public static string? FirstWrite(IEnumerable<JsonArray> calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        foreach (var c in calls)
        {
            var m = c.Count > 0 ? c[0].AsString() : null;
            if (m is null || !IsReadMethod(m))
            {
                return m ?? "(unnamed call)";
            }
        }

        return null;
    }
}
