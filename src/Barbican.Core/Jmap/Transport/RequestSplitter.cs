// Mirrors: Rust jmap.rs:104 split_calls; Bulwark lib/jmap/request-limits.ts:14 batched (going over a limit fails the whole request);
// RFC 8620 §2 (maxCallsInRequest), §3.7 (back-references resolve only inside their own request), §5.3 (creation ids, `#cid`). Owner jmap.

using System.Text.Json.Nodes;

namespace Barbican.Core.Jmap.Transport;

/// <summary>Splits method calls by <c>maxCallsInRequest</c>, unless they are linked by back-references or created ids.</summary>
public static class RequestSplitter
{
    /// <summary>The batches to send, in order; one batch when the calls fit or reference each other.</summary>
    public static IReadOnlyList<IReadOnlyList<JsonNode>> SplitCalls(IReadOnlyList<JsonNode> calls, int max)
    {
        ArgumentNullException.ThrowIfNull(calls);
        var size = Math.Max(1, max);
        if (calls.Count <= size || calls.Any(IsLinked))
        {
            return [calls];
        }

        return [.. calls.Chunk(size).Select(c => (IReadOnlyList<JsonNode>)c)];
    }

    /// <summary>True when a call's arguments use a back-reference (<c>#ids</c>) or a creation id (a <c>"#…"</c> string value).</summary>
    public static bool IsLinked(JsonNode call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (call is not JsonArray a || a.Count < 2 || a[1] is not JsonObject args)
        {
            return false;
        }

        return args.Any(kv => kv.Key.StartsWith('#')) || args.ToJsonString().Contains("\"#", StringComparison.Ordinal);
    }
}
